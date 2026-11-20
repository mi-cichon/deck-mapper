using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeckMapper;

/// <summary>
/// Klawiatura ekranowa, która nie kradnie fokusu (WS_EX_NOACTIVATE) – klawisze trafiają do gry.
/// Gra musi działać w trybie okna bez ramki, inaczej pełny ekran przykryje klawiaturę.
/// </summary>
sealed class OnScreenKeyboard : Form
{
    enum Kind { Key, Char, Shift, Close }

    sealed record OskKey(Kind Kind, string Label, string? ShiftLabel = null, string? KeyName = null,
        char Lower = '\0', char Upper = '\0', float Width = 1f);

    const int WsExNoActivate = 0x08000000;
    const int WsExToolWindow = 0x00000080;
    const int WsExTopmost = 0x00000008;

    static OskKey K(string label, string key, string? shift = null, float w = 1f) => new(Kind.Key, label, shift, key, Width: w);
    static OskKey L(char c) => new(Kind.Key, c.ToString(), char.ToUpperInvariant(c).ToString(), char.ToUpperInvariant(c).ToString());
    static OskKey P(char lower, char upper) => new(Kind.Char, lower.ToString(), upper.ToString(), Lower: lower, Upper: upper);
    static OskKey[] Letters(string s) => s.Select(L).ToArray();

    static readonly OskKey[][] Rows =
    {
        new[] { K("Esc", "Esc"), K("1", "1", "!"), K("2", "2", "@"), K("3", "3", "#"), K("4", "4", "$"), K("5", "5", "%"),
                K("6", "6", "^"), K("7", "7", "&"), K("8", "8", "*"), K("9", "9", "("), K("0", "0", ")"),
                K("-", "-", "_"), K("=", "=", "+"), K("⌫", "Backspace", w: 2f) },
        new[] { K("Tab", "Tab", w: 1.5f) }.Concat(Letters("qwertyuiop"))
            .Concat(new[] { K("[", "[", "{"), K("]", "]", "}"), K("\\", "\\", "|", 1.5f) }).ToArray(),
        new[] { K("Caps", "CapsLock", w: 1.5f) }.Concat(Letters("asdfghjkl"))
            .Concat(new[] { K(";", ";", ":"), K("'", "'", "\""), K("Enter", "Enter", w: 2.5f) }).ToArray(),
        new[] { new OskKey(Kind.Shift, "⇧ Shift", Width: 2.25f) }.Concat(Letters("zxcvbnm"))
            .Concat(new[] { K(",", ",", "<"), K(".", ".", ">"), K("/", "/", "?"), new OskKey(Kind.Shift, "⇧ Shift", Width: 2.25f) }).ToArray(),
        new[] { P('ą', 'Ą'), P('ć', 'Ć'), P('ę', 'Ę'), P('ł', 'Ł'), P('ń', 'Ń'), P('ó', 'Ó'), P('ś', 'Ś'), P('ź', 'Ź'), P('ż', 'Ż') },
        new[] { new OskKey(Kind.Close, "✕ Ukryj", Width: 2f), K("Spacja", "Space", w: 6f),
                K("←", "Left"), K("↑", "Up"), K("↓", "Down"), K("→", "Right"), K("Del", "Delete", w: 1.5f) },
    };

    readonly List<List<(KeyCap Cap, OskKey Key)>> _rows = new();
    Font? _keyFont;
    bool _shift;

    public OnScreenKeyboard()
    {
        Text = "DeckMapper – klawiatura";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(24, 24, 28);
        Opacity = 0.94;
        DoubleBuffered = true;

        foreach (var row in Rows)
        {
            var caps = new List<(KeyCap, OskKey)>();
            foreach (var key in row)
            {
                var cap = new KeyCap { Text = key.Label };
                cap.Pressed += (_, _) => OnKey(key);
                Controls.Add(cap);
                caps.Add((cap, key));
            }
            _rows.Add(caps);
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTopmost;
            return cp;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool EnableWindow(IntPtr hWnd, bool enable);

    /// <summary>
    /// Pokazuje klawiaturę do pisania w polu tekstowym naszego okna dialogowego. ShowDialog wyłącza
    /// pozostałe okna wątku UI (także klawiaturę), więc trzeba ją jawnie włączyć z powrotem.
    /// </summary>
    public void ShowForTyping()
    {
        if (!Visible) ShowKeyboard();
        EnableWindow(Handle, true);
    }

    public void ShowKeyboard()
    {
        var area = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
        Width = (int)(area.Width * 0.96);
        Height = (int)(area.Height * 0.45);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - Height - 8;
        Show();
        LayoutKeys();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutKeys();
    }

    void LayoutKeys()
    {
        if (_rows.Count == 0 || ClientSize.Width < 50 || ClientSize.Height < 50) return;

        const int pad = 8;
        float maxUnits = Rows.Max(r => r.Sum(k => k.Width));
        float unit = (ClientSize.Width - pad * 2) / maxUnits;
        float rowHeight = (ClientSize.Height - pad * 2) / (float)_rows.Count;

        // Nową czcionkę tworzymy tylko przy zmianie rozmiaru, a starą zwalniamy dopiero
        // po przepięciu wszystkich klawiszy. Setter Control.Font ignoruje czcionkę „równą”
        // starej, więc wcześniej klawisze zostawały ze zwolnioną czcionką i malowały czerwone X.
        float size = MathF.Round(Math.Max(10f, rowHeight * 0.34f));
        Font? oldFont = null;
        if (_keyFont == null || Math.Abs(_keyFont.Size - size) > 0.1f)
        {
            oldFont = _keyFont;
            _keyFont = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        SuspendLayout();
        for (int r = 0; r < _rows.Count; r++)
        {
            float rowUnits = _rows[r].Sum(k => k.Key.Width);
            float x = pad + (ClientSize.Width - pad * 2 - rowUnits * unit) / 2f;
            int y = (int)(pad + r * rowHeight);

            foreach (var (cap, key) in _rows[r])
            {
                float w = key.Width * unit;
                cap.Bounds = new Rectangle((int)x, y, (int)w, (int)rowHeight);
                cap.Font = _keyFont;
                x += w;
            }
        }
        ResumeLayout();
        oldFont?.Dispose();
    }

    void OnKey(OskKey key)
    {
        switch (key.Kind)
        {
            case Kind.Shift:
                SetShift(!_shift);
                return;
            case Kind.Close:
                Hide();
                return;
            case Kind.Char:
                InputSender.TypeChar(_shift ? key.Upper : key.Lower);
                break;
            case Kind.Key when key.KeyName != null && KeyNames.TryGet(key.KeyName, out var k):
                if (_shift)
                {
                    InputSender.KeyDown(KeyNames.LeftShift);
                    InputSender.TapKey(k);
                    InputSender.KeyUp(KeyNames.LeftShift);
                }
                else
                {
                    InputSender.TapKey(k);
                }
                break;
        }

        if (_shift) SetShift(false); // Shift działa na jeden znak, jak w telefonie
    }

    void SetShift(bool on)
    {
        _shift = on;
        foreach (var row in _rows)
            foreach (var (cap, key) in row)
            {
                cap.Text = on && key.ShiftLabel != null ? key.ShiftLabel : key.Label;
                if (key.Kind == Kind.Shift) cap.Active = on;
            }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _keyFont?.Dispose();
        base.Dispose(disposing);
    }
}