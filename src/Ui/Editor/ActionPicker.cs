using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace DeckMapper;

/// <summary>Siatka klawiszy jak na klawiaturze (z nawigacją i numeryczną), rysowana jako jedna kontrolka.</summary>
sealed class KeyboardGrid : Control
{
    int _pressed = -1;

    public KeyboardGrid()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
        TabStop = false;
        BackColor = Theme.Back;
    }

    public string? Selected { get; set; }
    public event Action<string>? KeyClicked;

    (float Unit, float OffsetX, float OffsetY) Metrics()
    {
        float unit = Math.Min(Width / KeyCatalog.GridWidth, Height / KeyCatalog.GridHeight);
        return (unit, (Width - unit * KeyCatalog.GridWidth) / 2f, (Height - unit * KeyCatalog.GridHeight) / 2f);
    }

    Rectangle BoundsOf(KeyCatalog.GridKey key)
    {
        var (u, ox, oy) = Metrics();
        return Rectangle.Round(new RectangleF(ox + key.X * u, oy + key.Y * u, key.W * u, key.H * u));
    }

    int HitTest(Point p)
    {
        for (int i = 0; i < KeyCatalog.Grid.Count; i++)
            if (BoundsOf(KeyCatalog.Grid[i]).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _pressed = HitTest(e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int index = _pressed;
        _pressed = -1;
        Invalidate();
        if (index >= 0 && index == HitTest(e.Location)) KeyClicked?.Invoke(KeyCatalog.Grid[index].Name);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var (unit, _, _) = Metrics();
        using var font = new Font("Segoe UI", Math.Max(7f, unit * 0.26f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var normal = new SolidBrush(Theme.Button);
        using var pressed = new SolidBrush(ControlPaint.Light(Theme.Accent, 0.3f));
        using var selected = new SolidBrush(Theme.Accent);

        for (int i = 0; i < KeyCatalog.Grid.Count; i++)
        {
            var key = KeyCatalog.Grid[i];
            var r = BoundsOf(key);
            r.Inflate(-2, -2);
            bool isSelected = string.Equals(key.Name, Selected, StringComparison.OrdinalIgnoreCase);
            using (var path = Gfx.RoundedRect(r, Math.Max(3, (int)(unit * 0.12f))))
                g.FillPath(i == _pressed ? pressed : isSelected ? selected : normal, path);
            TextRenderer.DrawText(g, key.Label, font, r, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>
/// Wybór akcji bez pisania: klawiatura (z modyfikatorami), mysz, akcje specjalne, tekst.
/// Zwraca tekst akcji w formacie pliku profilu ("" = brak).
/// </summary>
sealed class ActionPicker : DarkDialog
{
    enum Kind { None, Chord, Special, Text, Raw }
    enum Page { Keyboard, Mouse, Special, Text }

    readonly KeyboardHost _keyboard;
    readonly bool _keyboardWasVisible;
    readonly IReadOnlyList<string> _layers;
    readonly IReadOnlyList<string> _profiles;

    Kind _kind;
    bool _ctrl, _shift, _alt;
    string? _main;      // klawisz albo przycisk myszy (nazwa kanoniczna)
    string? _special;   // "@Keyboard", "@Layer:alt"...
    string _raw = "";   // akcja, której picker nie umie rozłożyć (np. "W+D") – zostaje bez zmian

    readonly Label _title, _subtitle, _current, _mouseNote;
    readonly DarkButton _ctrlButton, _shiftButton, _altButton;
    readonly Dictionary<Page, DarkButton> _tabs = new();
    readonly Dictionary<Page, Panel> _pages = new();
    readonly KeyboardGrid _grid;
    readonly List<(DarkButton Button, string Name)> _mouseButtons = new();
    readonly List<(DarkButton Button, string Action)> _specialButtons = new();
    readonly TextBox _textBox;
    readonly DarkButton _enterToggle;
    readonly DarkButton _clear, _cancel, _ok;
    bool _loading;

    ActionPicker(string controlName, string context, string? initial, IReadOnlyList<string> layers, IReadOnlyList<string> profiles,
        KeyboardHost keyboard)
    {
        _keyboard = keyboard;
        _keyboardWasVisible = keyboard.Visible;
        _layers = layers;
        _profiles = profiles;

        var area = WorkArea(null);
        Bounds = Rectangle.Inflate(area, -S(8), -S(8));

        _title = Theme.Label(controlName, Theme.Title);
        _subtitle = Theme.Label(context, Theme.Small, Theme.TextDim);
        _current = Theme.Label("", Theme.Big, Theme.Accent);
        Controls.AddRange(new Control[] { _title, _subtitle, _current });

        foreach (var (page, text) in new[] { (Page.Keyboard, "Klawiatura"), (Page.Mouse, "Mysz"), (Page.Special, "Specjalne"), (Page.Text, "Tekst") })
        {
            var tab = new DarkButton(text);
            tab.Click += (_, _) => ShowPage(page);
            _tabs[page] = tab;
            Controls.Add(tab);
            var panel = new Panel { BackColor = Theme.Back, Visible = false };
            _pages[page] = panel;
            Controls.Add(panel);
        }

        _ctrlButton = Modifier("Ctrl", () => _ctrl = !_ctrl);
        _shiftButton = Modifier("Shift", () => _shift = !_shift);
        _altButton = Modifier("Alt", () => _alt = !_alt);

        // --- klawiatura ---
        _grid = new KeyboardGrid();
        _grid.KeyClicked += name =>
        {
            _kind = Kind.Chord;
            _main = name;
            UpdateState();
        };
        _pages[Page.Keyboard].Controls.Add(_grid);

        // --- mysz ---
        _mouseNote = Theme.Label("Uwaga: MTA ignoruje programową mysz – tam te akcje nie działają. Kamera i strzelanie " +
                                 "zostają na lizard mode (prawy touchpad = kursor, R2 = lewy klik, L2 = prawy klik).",
            Theme.Small, Theme.Warning);
        _mouseNote.TextAlign = ContentAlignment.TopLeft;
        _pages[Page.Mouse].Controls.Add(_mouseNote);
        foreach (var (label, name) in KeyCatalog.Mouse)
        {
            var b = new DarkButton(label);
            b.Click += (_, _) =>
            {
                _kind = Kind.Chord;
                _main = name;
                UpdateState();
            };
            _mouseButtons.Add((b, name));
            _pages[Page.Mouse].Controls.Add(b);
        }

        // --- specjalne ---
        AddSpecial("Klawiatura ekranowa", "pokaż / ukryj", "@Keyboard");
        AddSpecial("Wstrzymaj / wznów", "mapowanie", "@Pause");
        AddSpecial("Otwórz edytor", "to okno", "@Editor");
        AddSpecial("Następny profil", "alfabetycznie", "@NextProfile");
        foreach (var layer in layers) AddSpecial($"Warstwa: {layer}", "trzymaj = warstwa aktywna", "@Layer:" + layer);
        foreach (var profile in profiles) AddSpecial($"Profil: {profile}", "przełącz na ten profil", "@Profile:" + profile);

        // --- tekst ---
        var textInfo = Theme.Label("Tekst wpisywany po wciśnięciu przycisku (np. komenda na czacie).", Theme.Small, Theme.TextDim);
        _textBox = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            Font = Theme.Big,
            BackColor = Theme.Row,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical,
        };
        _textBox.TextChanged += (_, _) =>
        {
            if (_loading) return;
            _kind = Kind.Text;
            UpdateState();
        };
        var showKeyboard = new DarkButton("Pokaż klawiaturę");
        showKeyboard.Click += (_, _) =>
        {
            _keyboard.ShowForTyping();
            _textBox.Focus();
        };
        _enterToggle = new DarkButton("Enter na końcu");
        _enterToggle.Click += (_, _) =>
        {
            _enterToggle.Checked = !_enterToggle.Checked;
            _kind = Kind.Text;
            UpdateState();
        };
        _pages[Page.Text].Controls.AddRange(new Control[] { textInfo, _textBox, showKeyboard, _enterToggle });
        _pages[Page.Text].Resize += (_, _) =>
        {
            var p = _pages[Page.Text];
            int w = p.Width;
            textInfo.Bounds = new Rectangle(0, 0, w, S(28));
            _textBox.Bounds = new Rectangle(0, S(30), w - S(230), S(110));
            showKeyboard.Bounds = new Rectangle(w - S(220), S(30), S(220), S(54));
            _enterToggle.Bounds = new Rectangle(w - S(220), S(88), S(220), S(54));
        };

        // --- dół ---
        _clear = new DarkButton("Wyczyść");
        _clear.Click += (_, _) =>
        {
            _kind = Kind.None;
            _ctrl = _shift = _alt = false;
            _main = _special = null;
            _loading = true;
            _textBox.Text = "";
            _enterToggle.Checked = false;
            _loading = false;
            UpdateState();
        };
        _cancel = new DarkButton("Anuluj");
        _cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        _ok = new DarkButton("OK") { Kind = DarkButton.Style.Primary };
        _ok.Click += (_, _) => DialogResult = DialogResult.OK;
        Controls.AddRange(new Control[] { _clear, _cancel, _ok });

        LoadAction(initial);
        LayoutAll();
    }

    DarkButton Modifier(string text, Action toggle)
    {
        var b = new DarkButton(text);
        b.Click += (_, _) =>
        {
            toggle();
            if (_kind != Kind.Chord)
            {
                _kind = Kind.Chord;
                _main = null;
            }
            UpdateState();
        };
        Controls.Add(b);
        return b;
    }

    void AddSpecial(string text, string sub, string action)
    {
        var b = new DarkButton(text) { SubText = sub };
        b.Click += (_, _) =>
        {
            _kind = Kind.Special;
            _special = action;
            UpdateState();
        };
        _specialButtons.Add((b, action));
        _pages[Page.Special].Controls.Add(b);
    }

    // ---------- stan ----------

    void LoadAction(string? action)
    {
        _loading = true;
        _kind = Kind.None;
        var page = Page.Keyboard;

        if (!KeyCatalog.IsNone(action))
        {
            var a = action!;
            if (a.StartsWith("Text:", StringComparison.OrdinalIgnoreCase))
            {
                var text = a[5..];
                _enterToggle.Checked = text.EndsWith('\n');
                if (_enterToggle.Checked) text = text[..^1];
                _textBox.Text = text.Replace("\r", "").Replace("\n", "\r\n");
                _kind = Kind.Text;
                page = Page.Text;
            }
            else if (a.TrimStart().StartsWith('@'))
            {
                _special = a.Trim();
                _kind = Kind.Special;
                page = Page.Special;
            }
            else if (TrySplitChord(a))
            {
                _kind = Kind.Chord;
                if (_main != null && KeyCatalog.CanonicalMouse(_main) != null) page = Page.Mouse;
            }
            else
            {
                _raw = a.Trim();
                _kind = Kind.Raw;
            }
        }

        _loading = false;
        ShowPage(page);
        UpdateState();
    }

    /// <summary>"Ctrl+Shift+F1" → modyfikatory + jeden klawisz. false, gdy akcja jest inna (np. "W+D").</summary>
    bool TrySplitChord(string action)
    {
        bool ctrl = false, shift = false, alt = false;
        string? main = null;
        foreach (var part in action.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var key = KeyCatalog.CanonicalKey(part);
            if (key == "LCtrl" && !ctrl && main == null && !IsLast(action, part)) { ctrl = true; continue; }
            if (key == "LShift" && !shift && main == null && !IsLast(action, part)) { shift = true; continue; }
            if (key == "LAlt" && !alt && main == null && !IsLast(action, part)) { alt = true; continue; }
            if (main != null) return false;
            main = key ?? KeyCatalog.CanonicalMouse(part);
            if (main == null) return false;
        }
        if (main == null) return false;
        (_ctrl, _shift, _alt, _main) = (ctrl, shift, alt, main);
        return true;
    }

    static bool IsLast(string action, string part) =>
        action.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Last() == part;

    public string Result => _kind switch
    {
        Kind.Chord => string.Join("+", new[] { _ctrl ? "Ctrl" : null, _shift ? "Shift" : null, _alt ? "Alt" : null, _main }
            .Where(p => p != null)),
        Kind.Special => _special ?? "",
        Kind.Text => "Text:" + _textBox.Text.Replace("\r", "") + (_enterToggle.Checked ? "\n" : ""),
        Kind.Raw => _raw,
        _ => "",
    };

    void UpdateState()
    {
        bool chord = _kind == Kind.Chord;
        _ctrlButton.Checked = chord && _ctrl;
        _shiftButton.Checked = chord && _shift;
        _altButton.Checked = chord && _alt;

        _grid.Selected = chord ? _main : null;
        _grid.Invalidate();
        foreach (var (b, name) in _mouseButtons) b.Checked = chord && name == _main;
        foreach (var (b, action) in _specialButtons)
            b.Checked = _kind == Kind.Special && string.Equals(action, _special, StringComparison.OrdinalIgnoreCase);

        var result = Result;
        if (_kind == Kind.Text && result == "Text:") result = "";
        string text = KeyCatalog.IsNone(result) ? "Brak akcji" : KeyCatalog.Describe(result);
        if (KeyCatalog.UsesMouse(result)) text += "   (mysz – w MTA nie działa)";
        _current.Text = "Akcja: " + text;
        _current.ForeColor = KeyCatalog.IsNone(result) ? Theme.TextDim : Theme.Accent;
    }

    void ShowPage(Page page)
    {
        foreach (var (p, tab) in _tabs) tab.Checked = p == page;
        foreach (var (p, panel) in _pages) panel.Visible = p == page;
        if (page == Page.Text) _textBox.Focus();
    }

    // ---------- układ ----------

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_ok != null) LayoutAll();
    }

    void LayoutAll()
    {
        int pad = S(16), gap = S(8), h = S(56);
        int w = ClientSize.Width - pad * 2;

        _title.Bounds = new Rectangle(pad, pad, w / 2, S(34));
        _subtitle.Bounds = new Rectangle(pad, _title.Bottom, w / 2, S(22));
        _current.Bounds = new Rectangle(pad + w / 2, pad, w / 2, S(56));
        _current.TextAlign = ContentAlignment.MiddleRight;

        // zakładki + modyfikatory w jednym rzędzie
        int y = _subtitle.Bottom + S(8);
        int tabW = Math.Min(S(170), (w - S(3 * 110) - gap * 7) / 4);
        int x = pad;
        foreach (var tab in _tabs.Values)
        {
            tab.Bounds = new Rectangle(x, y, tabW, h);
            x += tabW + gap;
        }
        int modW = S(110);
        x = pad + w - modW * 3 - gap * 2;
        foreach (var m in new[] { _ctrlButton, _shiftButton, _altButton })
        {
            m.Bounds = new Rectangle(x, y, modW, h);
            x += modW + gap;
        }

        int bottomY = ClientSize.Height - pad - h;
        int pageTop = y + h + S(12);
        var pageRect = new Rectangle(pad, pageTop, w, bottomY - S(12) - pageTop);
        foreach (var panel in _pages.Values) panel.Bounds = pageRect;

        _grid.Bounds = new Rectangle(0, 0, pageRect.Width, pageRect.Height);

        // mysz: notka + siatka 3 kolumny
        _mouseNote.Bounds = new Rectangle(0, 0, pageRect.Width, S(44));
        GridLayout(_mouseButtons.Select(m => (Control)m.Button).ToList(), new Rectangle(0, S(50), pageRect.Width, pageRect.Height - S(50)), 3, S(64));

        // specjalne: siatka 3 kolumny
        GridLayout(_specialButtons.Select(s => (Control)s.Button).ToList(), new Rectangle(0, 0, pageRect.Width, pageRect.Height), 3, S(64));

        int bw = (w - gap * 2) / 3;
        _clear.Bounds = new Rectangle(pad, bottomY, bw, h);
        _cancel.Bounds = new Rectangle(pad + bw + gap, bottomY, bw, h);
        _ok.Bounds = new Rectangle(pad + (bw + gap) * 2, bottomY, bw, h);
    }

    void GridLayout(List<Control> controls, Rectangle area, int columns, int height)
    {
        int gap = S(8);
        int rows = (controls.Count + columns - 1) / columns;
        // dużo warstw/profili – zmniejsz wysokość, żeby wszystko się zmieściło
        if (rows > 0) height = Math.Max(S(48), Math.Min(height, (area.Height - gap * (rows - 1)) / rows));
        int cw = (area.Width - gap * (columns - 1)) / columns;
        for (int i = 0; i < controls.Count; i++)
            controls[i].Bounds = new Rectangle(area.X + (i % columns) * (cw + gap), area.Y + (i / columns) * (height + gap), cw, height);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Enter z lizard mode (A) nie może zamknąć okna; w polu tekstowym działa normalnie
        if (e.KeyCode == Keys.Enter && !_textBox.Focused)
        {
            e.Handled = e.SuppressKeyPress = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (!_keyboardWasVisible) _keyboard.Hide();
        base.OnFormClosed(e);
    }

    /// <summary>Zwraca nową akcję ("" = brak) albo null, gdy anulowano.</summary>
    public static string? Pick(IWin32Window? owner, string controlName, string context, string? initial,
        IReadOnlyList<string> layers, IReadOnlyList<string> profiles, KeyboardHost keyboard)
    {
        using var picker = new ActionPicker(controlName, context, initial, layers, profiles, keyboard);
        if (picker.ShowDialog(owner) != DialogResult.OK) return null;
        var result = picker.Result;
        return result == "Text:" ? "" : result;
    }
}
