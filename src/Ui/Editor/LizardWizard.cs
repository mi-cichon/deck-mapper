using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeckMapper;

/// <summary>
/// Kreator „Wykryj mapę lizarda”: prosi o wciśnięcie każdego przycisku po kolei i zapisuje, co lizard mode
/// wtedy wysyła (klawisz albo sprzętowy klik myszy). Klawisze i kliki odczytuje własny hook LL – liczą się tylko
/// zdarzenia sprzętowe (nie SendInput, nie ekran dotykowy). Na czas kreatora mapowanie i filtr są wstrzymane,
/// a sprzętowe klawisze i kliki myszy połykane, żeby np. Enter pod A albo klik R2 nie „klikały” przycisków
/// kreatora – kreator obsługuje się ekranem dotykowym.
/// Zdarzenia lizarda mogą przyjść, zanim timer zauważy wciśnięcie w raporcie kontrolera, więc hook zapamiętuje
/// ostatnie zdarzenia, a początek przechwytywania dolicza te sprzed PreRollMs.
/// </summary>
sealed class LizardWizard : DarkDialog
{
    // kolejność kroków: najpierw to, co lizard zwykle mapuje, na końcu przyciski systemowe
    static readonly DeckControl[] Steps =
    {
        DeckControl.A, DeckControl.B, DeckControl.X, DeckControl.Y,
        DeckControl.DpadUp, DeckControl.DpadDown, DeckControl.DpadLeft, DeckControl.DpadRight,
        DeckControl.LB, DeckControl.RB, DeckControl.LT, DeckControl.RT,
        DeckControl.L3, DeckControl.R3, DeckControl.L4, DeckControl.L5, DeckControl.R4, DeckControl.R5,
        DeckControl.LeftPadClick, DeckControl.RightPadClick,
        DeckControl.View, DeckControl.Menu, DeckControl.Steam, DeckControl.QuickAccess,
    };

    const int SettleMs = 250;  // po puszczeniu czekamy chwilę na spóźnione zdarzenia
    const int PreRollMs = 300; // zdarzenia sprzed zauważenia wciśnięcia, które też liczymy

    // ---------- Win32 hooki ----------
    const int WhKeyboardLl = 13, WhMouseLl = 14;
    const int LlkhfExtended = 0x01, LlkhfInjected = 0x10, LlmhfInjected = 0x01;
    const long TouchSignatureMask = 0xFFFFFF00, TouchSignature = 0xFF515700; // MI_WP_SIGNATURE – dotyk/pióro

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? name);

    readonly HookProc _keyboardProc, _mouseProc;
    IntPtr _keyboardHook, _mouseHook;

    // ---------- stan ----------
    readonly ControllerWorker _worker;
    readonly LizardService _lizard;
    readonly bool _wasSuspended;
    readonly Dictionary<DeckControl, LizardOutput?> _results = new(); // null = pominięte
    readonly DeckState _state = new();
    int _index;
    bool _needRelease;      // przycisk z poprzedniego kroku jeszcze trzymany
    bool _capturing;        // docelowy przycisk wciśnięty – zbieramy zdarzenia
    long _releasedAt;       // kiedy puszczono docelowy przycisk (0 = trzymany)
    readonly List<KeyDef> _keys = new();
    string? _mouse;
    readonly List<(long Time, KeyDef? Key, string? Mouse)> _recent = new(); // zdarzenia poza przechwytywaniem

    // ---------- UI ----------
    readonly Label _title, _step, _instruction, _hint, _status;
    readonly ProgressStrip _progress;
    readonly DarkButton _back, _skip, _cancel, _save, _again;
    readonly SimpleList _summary;
    readonly Timer _timer;

    LizardWizard(ControllerWorker worker, LizardService lizard)
    {
        _worker = worker;
        _lizard = lizard;
        _wasSuspended = worker.Suspended;
        _keyboardProc = KeyboardHook;
        _mouseProc = MouseHook;

        Bounds = Rectangle.Inflate(WorkArea(null), -S(8), -S(8));

        _title = Theme.Label("Wykryj mapę lizarda", Theme.Title);
        _step = Theme.Label("", Theme.Normal, Theme.TextDim);
        _progress = new ProgressStrip();
        _instruction = Theme.Label("", new Font("Segoe UI", 34f, FontStyle.Bold));
        _instruction.TextAlign = ContentAlignment.MiddleCenter;
        _hint = Theme.Label("", Theme.Big, Theme.TextDim);
        _hint.TextAlign = ContentAlignment.TopCenter;
        _status = Theme.Label("", Theme.Big, Theme.Accent);
        _status.TextAlign = ContentAlignment.TopCenter;

        _back = new DarkButton("← Wstecz");
        _back.Click += (_, _) => GoTo(Math.Max(0, _index - 1));
        _skip = new DarkButton("Pomiń ten przycisk") { SubText = "zostanie poprzednia wartość" };
        _skip.Click += (_, _) => Record(null);
        _cancel = new DarkButton("Anuluj");
        _cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        _save = new DarkButton("Zapisz mapę") { Kind = DarkButton.Style.Primary };
        _save.Click += (_, _) => Save();
        _again = new DarkButton("Od początku");
        _again.Click += (_, _) =>
        {
            _results.Clear();
            GoTo(0);
        };
        _summary = new SimpleList();

        Controls.AddRange(new Control[] { _title, _step, _progress, _instruction, _hint, _status, _summary, _back, _skip, _cancel, _save, _again });

        _timer = new Timer { Interval = 20 };
        _timer.Tick += (_, _) => Poll();

        GoTo(0);
        LayoutAll();
    }

    /// <summary>Uruchamia kreator. true = zapisano nową mapę.</summary>
    public static bool Run(IWin32Window? owner, ControllerWorker worker, LizardService lizard)
    {
        using var wizard = new LizardWizard(worker, lizard);
        return wizard.ShowDialog(owner) == DialogResult.OK;
    }

    // ============================================================ cykl życia

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Log.Info("Kreator mapy lizarda: start");
        _worker.SetSuspended(true);
        _lizard.FilterPaused = true;
        var module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, module, 0);
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, module, 0);
        if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
            Log.Info($"Kreator mapy lizarda: nie udało się założyć hooków (błąd {Marshal.GetLastWin32Error()})");
        _timer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = IntPtr.Zero;
        _lizard.FilterPaused = false;
        _worker.SetSuspended(_wasSuspended);
        Log.Info($"Kreator mapy lizarda: koniec ({(DialogResult == DialogResult.OK ? "zapisano" : "anulowano")})");
        base.OnFormClosed(e);
    }

    // Esc z lizarda (B) nie może zamykać kreatora – hook i tak połyka sprzętowe klawisze, to dla pewności
    protected override void OnKeyDown(KeyEventArgs e) => e.Handled = e.SuppressKeyPress = true;

    // ============================================================ hooki (wątek UI, krótko)

    IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int flags = Marshal.ReadInt32(lParam, 8);
            if ((flags & LlkhfInjected) == 0)
            {
                int msg = (int)wParam;
                if (msg is 0x100 or 0x104)
                {
                    var key = new KeyDef((ushort)(Marshal.ReadInt32(lParam, 4) & 0xFF), (flags & LlkhfExtended) != 0);
                    if (!_capturing) _recent.Add((Environment.TickCount64, key, null));
                    else if (!_keys.Contains(key)) _keys.Add(key);
                }
                return (IntPtr)1; // sprzętowe klawisze nie docierają do okien podczas kreatora
            }
        }
        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = (int)wParam;
            if (msg is >= 0x201 and <= 0x209)
            {
                int flags = Marshal.ReadInt32(lParam, 12);
                long extra = Marshal.ReadIntPtr(lParam, IntPtr.Size == 8 ? 24 : 20).ToInt64();
                bool hardware = (flags & LlmhfInjected) == 0 && (extra & TouchSignatureMask) != TouchSignature;
                if (hardware)
                {
                    string? button = msg switch { 0x201 => "LMB", 0x204 => "RMB", 0x207 => "MMB", _ => null };
                    if (button != null)
                    {
                        if (!_capturing) _recent.Add((Environment.TickCount64, null, button));
                        else _mouse ??= button;
                    }
                    return (IntPtr)1; // sprzętowe kliki (lizard, mysz USB) nie klikają kreatora – obsługa dotykiem
                }
            }
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    // ============================================================ logika kroków

    static bool IsPressed(DeckState s, DeckControl c) => c switch
    {
        DeckControl.LT => s.LeftTrigger > 0.3f,
        DeckControl.RT => s.RightTrigger > 0.3f,
        _ => s.Buttons[(int)c],
    };

    void Poll()
    {
        if (_index >= Steps.Length) return;
        if (!_worker.Connected || !_worker.TryGetSnapshot(_state))
        {
            _status.Text = "Kontroler niepodłączony…";
            _status.ForeColor = Theme.Warning;
            return;
        }

        var target = Steps[_index];
        bool pressed = IsPressed(_state, target);
        long now = Environment.TickCount64;
        _recent.RemoveAll(e => now - e.Time > 1000);

        if (_needRelease)
        {
            if (!pressed) _needRelease = false;
            return;
        }

        if (!_capturing)
        {
            if (pressed)
            {
                _capturing = true;
                _releasedAt = 0;
                _keys.Clear();
                _mouse = null;
                foreach (var e in _recent.Where(e => now - e.Time <= PreRollMs))
                {
                    if (e.Key is { } k && !_keys.Contains(k)) _keys.Add(k);
                    if (e.Mouse != null) _mouse ??= e.Mouse;
                }
                _recent.Clear();
                _status.Text = "Trzymasz… puść";
                _status.ForeColor = Theme.Accent;
            }
            else
            {
                var other = Steps.FirstOrDefault(c => c != target && IsPressed(_state, c), target);
                if (other != target)
                {
                    _status.Text = $"To {ControlInfo.Of(other).Name} – wciśnij {ControlInfo.Of(target).Name}";
                    _status.ForeColor = Theme.Warning;
                }
            }
            return;
        }

        if (pressed)
        {
            _releasedAt = 0;
            return;
        }
        if (_releasedAt == 0)
        {
            _releasedAt = now;
            return;
        }
        if (now - _releasedAt < SettleMs) return;

        _capturing = false;
        LizardOutput result = _keys.Count > 0 ? LizardOutput.OfKey(_keys[0])
            : _mouse != null ? LizardOutput.OfMouse(_mouse)
            : LizardOutput.None;
        if (_keys.Count > 1)
            Log.Info($"Kreator mapy lizarda: {target} wysłał kilka klawiszy ({string.Join(", ", _keys.Select(LizardKeys.NameOf))}) – zapisuję pierwszy");
        Record(result);
    }

    void Record(LizardOutput? output)
    {
        if (_index >= Steps.Length) return;
        _results[Steps[_index]] = output;
        _capturing = false;
        GoTo(_index + 1);
    }

    void GoTo(int index)
    {
        _index = index;
        _capturing = false;
        _needRelease = _index > 0 && _index <= Steps.Length && _worker.TryGetSnapshot(_state) &&
                       Steps.Any(c => IsPressed(_state, c));

        bool summary = _index >= Steps.Length;
        _instruction.Visible = _hint.Visible = _status.Visible = _skip.Visible = !summary;
        _summary.Visible = _save.Visible = _again.Visible = summary;
        _back.Enabled = _index > 0;
        _progress.Set(Math.Min(_index, Steps.Length), Steps.Length);

        if (summary)
        {
            _step.Text = "Gotowe – sprawdź wynik i zapisz. Kursor z prawego touchpada lizard daje zawsze (nie jest w mapie).";
            _summary.SetItems(Steps.Select(c =>
            {
                var info = ControlInfo.Of(c);
                if (!_results.TryGetValue(c, out var r) || r == null)
                    return new SimpleList.Item(info.Name, "pominięty – zostaje poprzednia wartość", false, Theme.TextFaint);
                return new SimpleList.Item(info.Name, Describe(r.Value), false, r.Value.Kind == LizardOutputKind.None ? Theme.TextDim : Theme.Accent);
            }).ToList());
        }
        else
        {
            var info = ControlInfo.Of(Steps[_index]);
            _step.Text = $"Krok {_index + 1} z {Steps.Length}. Wciśnij i puść przycisk na Decku. Mapowanie i filtr są wstrzymane – " +
                         "przyciski kreatora stukaj palcem na ekranie.";
            _instruction.Text = $"Wciśnij  {info.Symbol}";
            _hint.Text = info.Name + (info.Note != null && !info.Note.StartsWith("lizard") ? $" ({info.Note})" : "");
            _status.Text = _results.TryGetValue(Steps[_index], out var prev) && prev != null
                ? $"Poprzednio: {Describe(prev.Value)} – wciśnij, żeby sprawdzić jeszcze raz"
                : "Czekam na przycisk…";
            _status.ForeColor = Theme.TextDim;
        }
        LayoutAll();
    }

    static string Describe(LizardOutput o) => o.Kind switch
    {
        LizardOutputKind.Key => $"lizard wysyła klawisz {o.Name}",
        LizardOutputKind.MouseButton => $"sprzętowy klik {o.Name}",
        _ => "nic",
    };

    void Save()
    {
        // pominięte przyciski zachowują poprzednią wartość (z pliku albo z wbudowanej mapy)
        var outputs = new Dictionary<DeckControl, LizardOutput>(_lizard.Map.Outputs);
        foreach (var (control, output) in _results)
            if (output != null) outputs[control] = output.Value;

        try
        {
            _lizard.SaveMap(new LizardMap(outputs, verified: true, detectedAt: DateTime.Now));
            DialogResult = DialogResult.OK;
        }
        catch (Exception e)
        {
            Log.Error("Zapis mapy lizarda", e);
            MessageDialog.Info(this, "Nie udało się zapisać", e.Message);
        }
    }

    // ============================================================ układ

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_again != null) LayoutAll();
    }

    void LayoutAll()
    {
        int pad = S(20), gap = S(10), h = S(64);
        int w = ClientSize.Width - pad * 2;
        _title.Bounds = new Rectangle(pad, pad, w, S(40));
        _step.Bounds = new Rectangle(pad, _title.Bottom, w, S(28));
        _progress.Bounds = new Rectangle(pad, _step.Bottom + S(8), w, S(14));

        int bottom = ClientSize.Height - pad - h;
        int top = _progress.Bottom + S(16);
        int area = bottom - gap - top;
        _instruction.Bounds = new Rectangle(pad, top + area / 6, w, S(90));
        _hint.Bounds = new Rectangle(pad, _instruction.Bottom, w, S(40));
        _status.Bounds = new Rectangle(pad, _hint.Bottom + S(16), w, S(40));
        _summary.Bounds = new Rectangle(pad, top, w, area);

        var buttons = _summary.Visible ? new[] { _back, _again, _cancel, _save } : new[] { _back, _skip, _cancel };
        int bw = (w - gap * (buttons.Length - 1)) / buttons.Length;
        for (int i = 0; i < buttons.Length; i++) buttons[i].Bounds = new Rectangle(pad + i * (bw + gap), bottom, bw, h);
    }

    /// <summary>Pasek postępu kreatora.</summary>
    sealed class ProgressStrip : Control
    {
        int _value, _max = 1;

        public ProgressStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Back;
        }

        public void Set(int value, int max)
        {
            (_value, _max) = (value, Math.Max(1, max));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var all = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Gfx.RoundedRect(all, Height / 2))
            using (var brush = new SolidBrush(Theme.Button))
                g.FillPath(brush, path);
            int filled = (int)((Width - 1) * (_value / (double)_max));
            if (filled < Height) return;
            using (var path = Gfx.RoundedRect(new Rectangle(0, 0, filled, Height - 1), Height / 2))
            using (var brush = new SolidBrush(Theme.Accent))
                g.FillPath(brush, path);
        }
    }
}
