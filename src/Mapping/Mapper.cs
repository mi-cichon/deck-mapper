using System;
using System.Collections.Generic;

namespace DeckMapper;

/// <summary>
/// Zamienia stan kontrolera na zdarzenia klawiatury/myszy. Używany wyłącznie z wątku kontrolera.
/// </summary>
sealed class Mapper
{
    static readonly int Count = DeckControls.Count;

    CompiledConfig _config;
    readonly bool[] _previous = new bool[Count];
    readonly bool[] _current = new bool[Count];
    readonly BindingAction?[] _held = new BindingAction?[Count]; // co zostało wciśnięte – żeby puścić to samo
    readonly List<string> _layers = new();
    double _accX, _accY;

    // ile kontrolek trzyma dany klawisz / przycisk myszy (ten sam klawisz na kilku kontrolkach)
    readonly HeldCounter<KeyDef> _heldKeys = new();
    readonly HeldCounter<MouseButton> _heldMouse = new();

    public Mapper(CompiledConfig config) => _config = config;

    public CompiledConfig Config => _config;
    public bool Paused { get; private set; }

    /// <summary>Wstrzymanie na czas edycji (niezależne od pauzy użytkownika). Nic nie jest wysyłane.</summary>
    public bool Suspended { get; private set; }

    public event Action? KeyboardToggleRequested;
    public event Action<bool>? PausedChanged;
    /// <summary>Żądanie zmiany profilu: nazwa albo null = następny.</summary>
    public event Action<string?>? ProfileRequested;
    public event Action? EditorRequested;

    // ---------- koło profili (stan tylko w wątku kontrolera; do UI idą wyłącznie zdarzenia) ----------

    /// <summary>Sloty i martwe strefy koła – podmieniane przez worker (niemutowalna migawka).</summary>
    public WheelSnapshot Wheel { get; set; } = WheelSnapshot.Empty;
    int _wheelControl = -1;
    int? _wheelSector;
    WheelSnapshot _wheelOpen = WheelSnapshot.Empty;

    public bool WheelIsOpen => _wheelControl >= 0;
    /// <summary>Koło otwarte: sloty (nazwy profili / null).</summary>
    public event Action<IReadOnlyList<string?>>? WheelOpened;
    public event Action<int?>? WheelSectorChanged;
    /// <summary>Koło zamknięte: wybrany profil albo null (anulowano: środek / pusty slot / przerwanie).</summary>
    public event Action<string?>? WheelClosed;

    public void SetConfig(CompiledConfig config)
    {
        CancelWheel("zmiana profilu");
        ReleaseAll("zmiana/przeładowanie profilu", rearm: true);
        _config = config;
    }

    /// <summary>
    /// Włącza/wyłącza wstrzymanie na czas edycji. Przy wstrzymaniu puszcza wszystko. Przyciski trzymane
    /// w chwili wznowienia nie zadziałają, dopóki nie zostaną puszczone i wciśnięte ponownie.
    /// </summary>
    public void SetSuspended(bool suspended)
    {
        if (suspended == Suspended) return;
        Suspended = suspended;
        if (suspended) CancelWheel("edytor");
        if (suspended) ReleaseAll("edytor – wstrzymanie", rearm: false);
    }

    public void Process(DeckState state, double dt)
    {
        Evaluate(_config, state, _current);

        if (WheelIsOpen)
        {
            // koło otwarte: mapowanie wstrzymane, liczy się tylko wybór sektora i puszczenie przycisku koła
            ProcessWheel(state);
            Array.Copy(_current, _previous, Count);
            return;
        }

        if (!Suspended)
        {
            for (int i = 0; i < Count; i++)
                if (_previous[i] && !_current[i]) Release(i);
            for (int i = 0; i < Count; i++)
                if (!_previous[i] && _current[i])
                {
                    Press(i);
                    if (WheelIsOpen) break; // reszta wciśnięć z tej klatki nie może przejść obok koła
                }
        }

        Array.Copy(_current, _previous, Count);

        if (!Paused && !Suspended)
        {
            MoveMouse(_config.LeftStick, state.LeftX, state.LeftY, dt);
            MoveMouse(_config.RightStick, state.RightX, state.RightY, dt);
        }
    }

    public void TogglePause() => SetPaused(!Paused);

    /// <summary>
    /// Puszcza wszystko, co trzymamy (np. przy rozłączeniu albo zmianie configu).
    /// rearm = trzymane klawisze/warstwy wcisną się znowu w następnym obiegu, jeśli przycisk Decka nadal jest
    /// trzymany. Bez tego np. W na RT puszczało się „samo” i wracało dopiero po ponownym pociągnięciu spustu.
    /// Akcje jednorazowe (profil, tekst, klawiatura, pauza, edytor) NIE są ponawiane – trzymany @NextProfile
    /// przełączałby profile w kółko.
    /// </summary>
    public void ReleaseAll(string reason, bool rearm)
    {
        var held = new List<string>();
        for (int i = 0; i < Count; i++)
        {
            var action = _held[i];
            if (action is null) continue;
            held.Add(((DeckControl)i).ToString());
            Release(i);
            if (rearm && action is ChordAction or LayerAction) _previous[i] = false;
        }
        _layers.Clear();
        if (held.Count > 0)
            Log.Info($"Puszczono trzymane ({reason}): {string.Join(", ", held)}{(rearm ? " – wcisną się znowu, jeśli dalej trzymasz" : "")}");
    }

    /// <summary>Stan wszystkich kontrolek (także wirtualnych). Bez efektów ubocznych – używa go też podgląd w edytorze.</summary>
    public static void Evaluate(CompiledConfig config, DeckState s, bool[] c)
    {
        Array.Copy(s.Buttons, c, Count);

        c[(int)DeckControl.LT] = s.LeftTrigger >= config.TriggerThreshold;
        c[(int)DeckControl.RT] = s.RightTrigger >= config.TriggerThreshold;

        if (s.Buttons[(int)DeckControl.LeftPadClick])
            Directions(s.LeftPadX, s.LeftPadY, config.LeftPadDeadzone, c,
                DeckControl.LeftPadUp, DeckControl.LeftPadDown, DeckControl.LeftPadLeft, DeckControl.LeftPadRight);

        if (config.LeftStick.Mode == StickMode.Keys)
            Directions(s.LeftX, s.LeftY, config.StickDeadzone, c,
                DeckControl.LeftStickUp, DeckControl.LeftStickDown, DeckControl.LeftStickLeft, DeckControl.LeftStickRight);

        if (config.RightStick.Mode == StickMode.Keys)
            Directions(s.RightX, s.RightY, config.StickDeadzone, c,
                DeckControl.RightStickUp, DeckControl.RightStickDown, DeckControl.RightStickLeft, DeckControl.RightStickRight);
    }

    /// <summary>8 kierunków: składowa musi przekroczyć ~sin(22,5°) długości wychylenia.</summary>
    static void Directions(float x, float y, float deadzone, bool[] c,
        DeckControl up, DeckControl down, DeckControl left, DeckControl right)
    {
        float magnitude = MathF.Sqrt(x * x + y * y);
        if (magnitude < deadzone) return;

        float threshold = magnitude * 0.38f;
        c[(int)up] = y > threshold;
        c[(int)down] = y < -threshold;
        c[(int)left] = x < -threshold;
        c[(int)right] = x > threshold;
    }

    void Press(int index)
    {
        var action = Resolve(_config, _layers, (DeckControl)index);
        if (action is null) return;
        if (Paused && action is not PauseAction) return;

        _held[index] = action;
        switch (action)
        {
            case ChordAction chord:
                foreach (var token in chord.Tokens) Down(token);
                break;
            case TextAction text:
                InputSender.TypeText(text.Text);
                break;
            case KeyboardAction:
                KeyboardToggleRequested?.Invoke();
                break;
            case PauseAction:
                TogglePause();
                break;
            case LayerAction layer:
                _layers.Add(layer.Name);
                break;
            case ProfileAction profile:
                ProfileRequested?.Invoke(profile.Name);
                break;
            case NextProfileAction:
                ProfileRequested?.Invoke(null);
                break;
            case EditorAction:
                EditorRequested?.Invoke();
                break;
            case ProfileWheelAction:
                OpenWheel(index);
                break;
        }
    }

    void OpenWheel(int control)
    {
        var wheel = Wheel;
        if (wheel.Slots.Count < WheelSettings.MinSlots) return;
        _held[control] = null; // sam przycisk koła nie jest „trzymanym klawiszem”
        ReleaseAll("koło profili", rearm: false); // trzymane klawisze puszczone (np. W w aucie)
        _wheelControl = control;
        _wheelSector = null;
        _wheelOpen = wheel;
        Log.Info($"Koło profili: otwarte ({(DeckControl)control}, slotów: {wheel.Slots.Count})");
        WheelOpened?.Invoke(wheel.Slots);
    }

    void ProcessWheel(DeckState s)
    {
        if (!_current[_wheelControl])
        {
            var profile = WheelSelector.Result(_wheelOpen.Slots, _wheelSector);
            Log.Info($"Koło profili: puszczone – {(profile != null ? "wybrano " + profile : _wheelSector == null ? "środek, anulowano" : "pusty slot, anulowano")}");
            _wheelControl = -1;
            WheelClosed?.Invoke(profile);
            return;
        }

        var sector = WheelSelector.Select(s.RightX, s.RightY, s.LeftPadTouched, s.LeftPadX, s.LeftPadY,
            _wheelOpen.Slots.Count, _wheelOpen.Settings);
        if (sector == _wheelSector) return;
        _wheelSector = sector;
        WheelSectorChanged?.Invoke(sector);
    }

    void CancelWheel(string reason)
    {
        if (!WheelIsOpen) return;
        _wheelControl = -1;
        Log.Info($"Koło profili: przerwane ({reason})");
        WheelClosed?.Invoke(null);
    }

    void Release(int index)
    {
        var action = _held[index];
        if (action is null) return;
        _held[index] = null;

        switch (action)
        {
            case ChordAction chord:
                for (int k = chord.Tokens.Count - 1; k >= 0; k--) Up(chord.Tokens[k]);
                break;
            case LayerAction layer:
                int at = _layers.FindLastIndex(n => string.Equals(n, layer.Name, StringComparison.OrdinalIgnoreCase));
                if (at >= 0) _layers.RemoveAt(at);
                break;
        }
    }

    /// <summary>Akcja kontrolki przy danych aktywnych warstwach (ostatnia wciśnięta warstwa wygrywa).</summary>
    public static BindingAction? Resolve(CompiledConfig config, IReadOnlyList<string> layers, DeckControl control)
    {
        for (int k = layers.Count - 1; k >= 0; k--)
            if (config.Layers.TryGetValue(layers[k], out var layer) && layer.TryGetValue(control, out var action))
                return action;

        return config.Bindings.TryGetValue(control, out var baseAction) ? baseAction : null;
    }

    void SetPaused(bool paused)
    {
        if (paused == Paused) return;
        Paused = paused;

        if (paused)
            for (int i = 0; i < Count; i++)
                if (_held[i] is not null and not PauseAction) Release(i);

        PausedChanged?.Invoke(paused);
    }

    void Down(InputToken token)
    {
        switch (token)
        {
            case KeyToken k:
                if (_heldKeys.Press(k.Key)) InputSender.KeyDown(k.Key);
                break;
            case MouseToken m:
                if (_heldMouse.Press(m.Button)) InputSender.MouseButtonEvent(m.Button, true);
                break;
            case WheelToken w:
                InputSender.Wheel(w.Delta, w.Horizontal);
                break;
        }
    }

    void Up(InputToken token)
    {
        switch (token)
        {
            case KeyToken k:
                if (_heldKeys.Release(k.Key)) InputSender.KeyUp(k.Key);
                break;
            case MouseToken m:
                if (_heldMouse.Release(m.Button)) InputSender.MouseButtonEvent(m.Button, false);
                break;
        }
    }

    void MoveMouse(StickSettings stick, float x, float y, double dt)
    {
        if (stick.Mode != StickMode.Mouse) return;

        float deadzone = _config.StickDeadzone;
        float magnitude = MathF.Sqrt(x * x + y * y);
        if (magnitude < deadzone) return;

        float normalized = Math.Min(1f, (magnitude - deadzone) / (1f - deadzone));
        double speed = stick.MouseSpeed * Math.Pow(normalized, stick.Curve);

        _accX += x / magnitude * speed * dt;
        _accY += -y / magnitude * speed * dt;

        int dx = (int)_accX, dy = (int)_accY;
        if (dx == 0 && dy == 0) return;

        _accX -= dx;
        _accY -= dy;
        InputSender.MoveMouse(dx, dy);
    }
}

/// <summary>Migawka konfiguracji koła dla wątku kontrolera.</summary>
sealed record WheelSnapshot(IReadOnlyList<string?> Slots, WheelSettings Settings)
{
    public static readonly WheelSnapshot Empty = new(Array.Empty<string?>(), new WheelSettings());
}
