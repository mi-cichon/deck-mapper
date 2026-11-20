using System;
using System.Diagnostics;
using System.Threading;

namespace DeckMapper;

/// <summary>Wątek w tle: łączy się z kontrolerem, czyta raporty i karmi nimi Mapper.</summary>
sealed class ControllerWorker : IDisposable
{
    readonly Mapper _mapper;
    readonly Thread _thread;
    volatile bool _running = true;
    volatile bool _connected;

    CompiledConfig? _pendingConfig;
    int _pauseRequests;
    volatile bool _suspend;

    readonly object _snapshotLock = new();
    readonly DeckState _snapshot = new();
    bool _hasSnapshot;

    public ControllerWorker(Mapper mapper)
    {
        _mapper = mapper;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "DeckMapper.Controller",
            Priority = ThreadPriority.AboveNormal,
        };
    }

    public bool Connected => _connected;
    public event Action<bool>? ConnectionChanged;

    public void Start() => _thread.Start();

    public void ApplyConfig(CompiledConfig config) => Interlocked.Exchange(ref _pendingConfig, config);

    public void RequestTogglePause() => Interlocked.Increment(ref _pauseRequests);

    /// <summary>Wstrzymuje mapowanie na czas edycji (wątek kontrolera zastosuje to w następnym obiegu).</summary>
    public void SetSuspended(bool suspended) => _suspend = suspended;

    public bool TryGetSnapshot(DeckState target)
    {
        lock (_snapshotLock)
        {
            if (!_hasSnapshot) return false;
            _snapshot.CopyTo(target);
            return true;
        }
    }

    void Run()
    {
        using var controller = new SteamDeckController();
        var state = new DeckState();
        var clock = Stopwatch.StartNew();
        double lastFrame = 0, lastHeartbeat = -10;
        bool lizardCleared = false;

        while (_running)
        {
            try
            {
                if (!controller.IsConnected)
                {
                    SetConnected(false);
                    if (!controller.TryConnect())
                    {
                        Thread.Sleep(2000);
                        continue;
                    }
                    SetConnected(true);
                    lastHeartbeat = -10;
                    lastFrame = clock.Elapsed.TotalSeconds;
                }

                var pending = Interlocked.Exchange(ref _pendingConfig, null);
                if (pending != null)
                {
                    _mapper.SetConfig(pending);
                    if (!pending.DisableLizardButtons && lizardCleared)
                    {
                        controller.RestoreLizardButtons();
                        lizardCleared = false;
                    }
                }

                bool suspend = _suspend;
                if (suspend != _mapper.Suspended) _mapper.SetSuspended(suspend);

                while (Interlocked.Exchange(ref _pauseRequests, 0) > 0)
                    _mapper.TogglePause();

                double now = clock.Elapsed.TotalSeconds;

                // kontroler sam wraca do lizard mode po kilku sekundach – trzeba przypominać
                if (_mapper.Config.DisableLizardButtons && now - lastHeartbeat > 2)
                {
                    controller.ClearLizardButtons();
                    lastHeartbeat = now;
                    lizardCleared = true;
                }

                switch (controller.Read(state))
                {
                    case ReadResult.Disconnected:
                        _mapper.ReleaseAll();
                        continue;
                    case ReadResult.Timeout:
                        continue;
                }

                now = clock.Elapsed.TotalSeconds;
                double dt = Math.Clamp(now - lastFrame, 0, 0.05);
                lastFrame = now;

                _mapper.Process(state, dt);

                lock (_snapshotLock)
                {
                    state.CopyTo(_snapshot);
                    _hasSnapshot = true;
                }
            }
            catch (Exception e)
            {
                Log.Error("Pętla kontrolera", e);
                _mapper.ReleaseAll();
                Thread.Sleep(500);
            }
        }

        _mapper.ReleaseAll();
        if (lizardCleared) controller.RestoreLizardButtons();
    }

    void SetConnected(bool connected)
    {
        if (_connected == connected) return;
        _connected = connected;
        ConnectionChanged?.Invoke(connected);
    }

    public void Dispose()
    {
        _running = false;
        if (_thread.IsAlive) _thread.Join(1500);
    }
}
