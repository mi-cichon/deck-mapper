using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeckMapper;

/// <summary>
/// Globalny hook WH_KEYBOARD_LL połykający klawisze, które sam wysyła lizard mode (np. Enter pod A).
/// Połyka TYLKO zdarzenia sprzętowe (bez LLKHF_INJECTED) i tylko scancode'y z planu – nasze SendInput
/// (mapowanie, klawiatura ekranowa) przechodzą. Myszy nie dotyka. Test na Decku: hook LL połyka lizardowy
/// Enter przed MTA na serwerze (zakładany przed i po starcie MTA).
///
/// Hook żyje na własnym wątku z pętlą GetMessage; callback jest możliwie najkrótszy (bez alokacji i logowania),
/// bo Windows po cichu zdejmuje hooki LL przekraczające LowLevelHooksTimeout. Czas callbacku jest mierzony,
/// przekroczenia 1 ms trafiają do logu zbiorczo co minutę.
/// </summary>
sealed class LizardKeyFilter : IDisposable
{
    const int WhKeyboardLl = 13;
    const int LlkhfExtended = 0x01, LlkhfInjected = 0x10;
    const uint WmQuit = 0x0012, WmTimer = 0x0113;
    const int ReportIntervalMs = 60_000;
    static readonly long SlowTicks = Stopwatch.Frequency / 1000; // 1 ms

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct Msg { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint elapse, IntPtr proc);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? name);

    readonly HookProc _proc; // referencja – inaczej GC zebrałby delegata, a Windows wołałby zwolnioną pamięć
    volatile HashSet<int> _codes = new();
    volatile bool _paused;
    Thread? _thread;
    uint _threadId;
    IntPtr _hook;

    // statystyki (zapis z callbacku tylko przez Interlocked)
    int _swallowed, _slow;
    long _maxTicks;

    public LizardKeyFilter() => _proc = Callback;

    public bool Running => _thread != null;

    /// <summary>Kreator mapy lizarda: hook zostaje, ale niczego nie połyka.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            Log.Info($"Filtr lizarda: {(value ? "wstrzymany (kreator mapy)" : "wznowiony")}");
        }
    }

    /// <summary>Wywoływane z wątku UI. Zakłada/zdejmuje hook bez restartu aplikacji.</summary>
    public void Apply(SwallowPlan plan)
    {
        _codes = new HashSet<int>(plan.Codes); // nowa instancja – hook czyta ją bez blokady
        if (plan.Active && !Running) Start();
        else if (!plan.Active && Running) Stop();
    }

    void Start()
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Loop(ready))
        {
            IsBackground = true,
            Name = "DeckMapper.LizardKeyFilter",
            Priority = ThreadPriority.Highest,
        };
        _thread.Start();
        ready.Wait(2000);
    }

    void Stop()
    {
        var thread = _thread;
        if (thread == null) return;
        PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        if (!thread.Join(2000)) Log.Info("Filtr lizarda: wątek hooka nie zakończył się w 2 s");
        _thread = null;
    }

    void Loop(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        Log.Info(_hook != IntPtr.Zero
            ? "Filtr lizarda: hook klawiatury założony"
            : $"Filtr lizarda: NIE udało się założyć hooka (błąd {Marshal.GetLastWin32Error()})");
        SetTimer(IntPtr.Zero, UIntPtr.Zero, ReportIntervalMs, IntPtr.Zero);
        ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            if (msg.message == WmTimer) Report();

        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        Report();
        Log.Info("Filtr lizarda: hook klawiatury zdjęty");
    }

    void Report()
    {
        int swallowed = Interlocked.Exchange(ref _swallowed, 0);
        int slow = Interlocked.Exchange(ref _slow, 0);
        long max = Interlocked.Exchange(ref _maxTicks, 0);
        if (swallowed > 0) Log.Info($"Filtr lizarda: połknięto {swallowed} zdarzeń klawiatury");
        if (slow > 0)
            Log.Info($"Filtr lizarda: UWAGA – callback trwał > 1 ms {slow} razy (max {max * 1000.0 / Stopwatch.Frequency:0.00} ms). " +
                     "Zbyt wolny hook Windows zdejmuje bez ostrzeżenia.");
    }

    IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        long start = Stopwatch.GetTimestamp();
        bool swallow = false;

        if (code >= 0 && !_paused)
        {
            // KBDLLHOOKSTRUCT: vkCode @0, scanCode @4, flags @8 – czytamy pola bez kopiowania struktury
            int flags = Marshal.ReadInt32(lParam, 8);
            if ((flags & LlkhfInjected) == 0)
            {
                int scan = Marshal.ReadInt32(lParam, 4);
                swallow = _codes.Contains(LizardKeys.Code((uint)scan, (flags & LlkhfExtended) != 0));
            }
        }

        long elapsed = Stopwatch.GetTimestamp() - start;
        if (elapsed > SlowTicks)
        {
            Interlocked.Increment(ref _slow);
            long max;
            while (elapsed > (max = Interlocked.Read(ref _maxTicks)) && Interlocked.CompareExchange(ref _maxTicks, elapsed, max) != max) { }
        }

        if (swallow)
        {
            Interlocked.Increment(ref _swallowed);
            return (IntPtr)1;
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose() => Stop();
}
