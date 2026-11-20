using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeckMapper;

/// <summary>Bazowe okno dialogowe: ciemne, bez ramki systemowej, Esc = anuluj.</summary>
class DarkDialog : Form
{
    protected DarkDialog()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = Theme.Normal;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        DoubleBuffered = true;
    }

    protected int S(int logical) => LogicalToDeviceUnits(logical);

    protected static Rectangle WorkArea(Form? owner) =>
        (owner != null ? Screen.FromControl(owner) : Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;

    protected void CenterOn(Form? owner, int width, int height)
    {
        var area = WorkArea(owner);
        width = Math.Min(width, area.Width - S(16));
        height = Math.Min(height, area.Height - S(16));
        Bounds = new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
    }

    /// <summary>U góry ekranu – dolną część zasłania klawiatura ekranowa.</summary>
    protected void TopOn(Form? owner, int width, int height)
    {
        var area = WorkArea(owner);
        width = Math.Min(width, area.Width - S(16));
        Bounds = new Rectangle(area.Left + (area.Width - width) / 2, area.Top + S(12), width, height);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            DialogResult = DialogResult.Cancel;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.ButtonHover, S(2));
        e.Graphics.DrawRectangle(pen, 1, 1, Width - 2, Height - 2);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }
}

/// <summary>Pytanie z dużymi przyciskami. Zwraca indeks przycisku albo -1 (Esc).</summary>
sealed class MessageDialog : DarkDialog
{
    int _result = -1;

    MessageDialog(string title, string text, (string Label, DarkButton.Style Style)[] buttons)
    {
        var titleLabel = Theme.Label(title, Theme.Title);
        var textLabel = Theme.Label(text, Theme.Normal);
        textLabel.TextAlign = ContentAlignment.TopLeft;
        Controls.Add(titleLabel);
        Controls.Add(textLabel);

        var buttonControls = new List<DarkButton>();
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var b = new DarkButton(buttons[i].Label) { Kind = buttons[i].Style };
            b.Click += (_, _) =>
            {
                _result = index;
                DialogResult = DialogResult.OK;
            };
            buttonControls.Add(b);
            Controls.Add(b);
        }

        int width = S(560), pad = S(20);
        int textHeight = TextRenderer.MeasureText(text, Theme.Normal, new Size(width - pad * 2, 0), TextFormatFlags.WordBreak).Height + S(8);
        int height = pad + S(40) + textHeight + S(16) + S(60) + pad;
        CenterOn(null, width, height);

        titleLabel.Bounds = new Rectangle(pad, pad, Width - pad * 2, S(40));
        textLabel.Bounds = new Rectangle(pad, titleLabel.Bottom, Width - pad * 2, textHeight);
        int bw = (Width - pad * 2 - S(10) * (buttons.Length - 1)) / buttons.Length;
        for (int i = 0; i < buttonControls.Count; i++)
            buttonControls[i].Bounds = new Rectangle(pad + i * (bw + S(10)), Height - pad - S(60), bw, S(60));
    }

    public static int Ask(IWin32Window? owner, string title, string text, params (string, DarkButton.Style)[] buttons)
    {
        using var dialog = new MessageDialog(title, text, buttons);
        dialog.ShowDialog(owner);
        return dialog._result;
    }

    public static void Info(IWin32Window? owner, string title, string text) =>
        Ask(owner, title, text, ("OK", DarkButton.Style.Primary));
}

/// <summary>Pole tekstowe + klawiatura ekranowa (Deck nie ma fizycznej klawiatury).</summary>
sealed class TextPrompt : DarkDialog
{
    readonly TextBox _box;
    readonly Label _error;
    readonly Func<string, string?>? _validate;
    readonly Func<string, string>? _hint;
    readonly KeyboardHost _keyboard;
    readonly bool _keyboardWasVisible;

    TextPrompt(string title, string label, string initial, Func<string, string?>? validate, KeyboardHost keyboard,
        Func<string, string>? hint)
    {
        _validate = validate;
        _hint = hint;
        _keyboard = keyboard;
        _keyboardWasVisible = keyboard.Visible;

        int pad = S(20);
        TopOn(null, S(760), S(280));

        var titleLabel = Theme.Label(title, Theme.Title);
        titleLabel.Bounds = new Rectangle(pad, pad, Width - pad * 2, S(36));
        var textLabel = Theme.Label(label, Theme.Normal, Theme.TextDim);
        textLabel.Bounds = new Rectangle(pad, titleLabel.Bottom, Width - pad * 2, S(28));

        _box = new TextBox
        {
            Text = initial,
            Font = Theme.Big,
            BackColor = Theme.Row,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _box.Bounds = new Rectangle(pad, textLabel.Bottom + S(4), Width - pad * 2 - S(150), S(40));
        _box.TextChanged += (_, _) => ShowHint();

        var kb = new DarkButton("Klawiatura");
        kb.Bounds = new Rectangle(_box.Right + S(10), _box.Top - S(6), S(140), S(54));
        kb.Click += (_, _) => { _keyboard.ShowForTyping(); _box.Focus(); };

        _error = Theme.Label("", Theme.Small, Theme.Warning);
        _error.Bounds = new Rectangle(pad, _box.Bottom + S(6), Width - pad * 2, S(26));

        var cancel = new DarkButton("Anuluj");
        var ok = new DarkButton("OK") { Kind = DarkButton.Style.Primary };
        int bw = (Width - pad * 2 - S(10)) / 2;
        cancel.Bounds = new Rectangle(pad, Height - pad - S(60), bw, S(60));
        ok.Bounds = new Rectangle(cancel.Right + S(10), cancel.Top, bw, S(60));
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        ok.Click += (_, _) => Accept();

        Controls.AddRange(new Control[] { titleLabel, textLabel, _box, kb, _error, cancel, ok });
        ShowHint();
    }

    /// <summary>Podpowiedź na żywo pod polem (np. licznik „3/6”); błąd walidacji ją zastępuje.</summary>
    void ShowHint()
    {
        _error.ForeColor = Theme.TextDim;
        _error.Text = _hint?.Invoke(_box.Text) ?? "";
    }

    public string Value => _box.Text.Trim();

    void Accept()
    {
        var error = _validate?.Invoke(Value);
        if (error != null)
        {
            _error.ForeColor = Theme.Warning;
            _error.Text = error;
            return;
        }
        DialogResult = DialogResult.OK;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = e.SuppressKeyPress = true;
            Accept();
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _keyboard.ShowForTyping();
        _box.Focus();
        _box.SelectAll();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (!_keyboardWasVisible) _keyboard.Hide();
        base.OnFormClosed(e);
    }

    public static string? Ask(IWin32Window? owner, KeyboardHost keyboard, string title, string label, string initial = "",
        Func<string, string?>? validate = null, Func<string, string>? hint = null)
    {
        using var dialog = new TextPrompt(title, label, initial, validate, keyboard, hint);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Value : null;
    }
}

/// <summary>Wybór jednej pozycji z listy (profile, procesy). Zwraca indeks albo -1.</summary>
sealed class ChoiceDialog : DarkDialog
{
    readonly SimpleList _list;
    int _result = -1;

    ChoiceDialog(string title, List<SimpleList.Item> items)
    {
        int pad = S(20);
        CenterOn(null, S(620), S(600));

        var titleLabel = Theme.Label(title, Theme.Title);
        titleLabel.Bounds = new Rectangle(pad, pad, Width - pad * 2, S(40));

        _list = new SimpleList { Bounds = new Rectangle(pad, titleLabel.Bottom + S(8), Width - pad * 2, Height - titleLabel.Bottom - S(8) - S(60) - pad * 2) };
        _list.SetItems(items);
        _list.ItemClicked += i =>
        {
            _result = i;
            DialogResult = DialogResult.OK;
        };

        var cancel = new DarkButton("Anuluj");
        cancel.Bounds = new Rectangle(pad, Height - pad - S(60), Width - pad * 2, S(60));
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[] { titleLabel, _list, cancel });
    }

    public static int Ask(IWin32Window? owner, string title, IEnumerable<SimpleList.Item> items)
    {
        using var dialog = new ChoiceDialog(title, items.ToList());
        dialog.ShowDialog(owner);
        return dialog._result;
    }
}
