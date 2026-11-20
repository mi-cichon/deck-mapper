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

    public void SetConfig(CompiledConfig config)
    {
        ReleaseAll();
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
        if (suspended) ReleaseAll();
    }

    public void Process(DeckState state, double dt)
    {
        Evaluate(_config, state, _current);

        if (!Suspended)
        {
            for (int i = 0; i < Count; i++)
                if (_previous[i] && !_current[i]) Release(i);
            for (int i = 0; i < Count; i++)
                if (!_previous[i] && _current[i]) Press(i);
        }

        Array.Copy(_current, _previous, Count);

        if (!Paused && !Suspended)
        {
            MoveMouse(_config.LeftStick, state.LeftX, state.LeftY, dt);
            MoveMouse(_config.RightStick, state.RightX, state.RightY, dt);
        }
    }

    public void TogglePause() => SetPaused(!Paused);

    /// <summary>Puszcza wszystko, co trzymamy (np. przy rozłączeniu albo zmianie configu).</summary>
    public void ReleaseAll()
    {
        for (int i = 0; i < Count; i++) Release(i);
        _layers.Clear();
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
        }
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

    static void Down(InputToken token)
    {
        switch (token)
        {
            case KeyToken k: InputSender.KeyDown(k.Key); break;
            case MouseToken m: InputSender.MouseButtonEvent(m.Button, true); break;
            case WheelToken w: InputSender.Wheel(w.Delta, w.Horizontal); break;
        }
    }

    static void Up(InputToken token)
    {
        switch (token)
        {
            case KeyToken k: InputSender.KeyUp(k.Key); break;
            case MouseToken m: InputSender.MouseButtonEvent(m.Button, false); break;
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
