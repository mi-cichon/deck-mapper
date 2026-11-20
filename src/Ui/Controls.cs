using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeckMapper;

/// <summary>
/// Duży przycisk pod palec. Nie przyjmuje fokusu – Enter/Esc/strzałki, które kontroler wysyła
/// w lizard mode, nie „klikną” go przypadkiem.
/// </summary>
sealed class DarkButton : Control
{
    public enum Style { Normal, Primary, Danger }

    bool _hover, _pressed, _checked;
    Style _style;

    public DarkButton(string text = "")
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
        TabStop = false;
        Text = text;
        Font = Theme.Normal;
        ForeColor = Theme.Text;
        BackColor = Color.Transparent;
    }

    public bool Checked
    {
        get => _checked;
        set { if (_checked != value) { _checked = value; Invalidate(); } }
    }

    public Style Kind
    {
        get => _style;
        set { _style = value; Invalidate(); }
    }

    /// <summary>Mała druga linia pod tekstem (np. opis).</summary>
    public string? SubText { get; set; }

    public ContentAlignment TextAlign { get; set; } = ContentAlignment.MiddleCenter;

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Back);

        var rect = new Rectangle(2, 2, Math.Max(1, Width - 5), Math.Max(1, Height - 5));
        Color fill = _style switch
        {
            Style.Primary => Theme.Accent,
            Style.Danger => Theme.Danger,
            _ => _checked ? Theme.Checked : Theme.Button,
        };
        if (!Enabled) fill = Color.FromArgb(38, 38, 44);
        else if (_pressed) fill = ControlPaint.Light(fill, 0.4f);
        else if (_hover) fill = ControlPaint.Light(fill, 0.15f);

        using (var path = Gfx.RoundedRect(rect, LogicalToDeviceUnits(8)))
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);

        if (_checked && _style == Style.Normal && Enabled)
            using (var path = Gfx.RoundedRect(rect, LogicalToDeviceUnits(8)))
            using (var pen = new Pen(Theme.Accent, LogicalToDeviceUnits(2)))
                g.DrawPath(pen, path);

        var color = Enabled ? ForeColor : Theme.TextFaint;
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis |
                    (TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
        var inner = Rectangle.Inflate(rect, -LogicalToDeviceUnits(10), -LogicalToDeviceUnits(2));

        if (string.IsNullOrEmpty(SubText))
        {
            TextRenderer.DrawText(g, Text, Font, inner, color, flags | TextFormatFlags.VerticalCenter);
        }
        else
        {
            int h1 = TextRenderer.MeasureText(Text, Font).Height;
            int h2 = TextRenderer.MeasureText(SubText, Theme.Small).Height;
            int top = inner.Top + (inner.Height - h1 - h2) / 2;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(inner.Left, top, inner.Width, h1), color, flags | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, SubText, Theme.Small, new Rectangle(inner.Left, top + h1, inner.Width, h2),
                Enabled ? Theme.TextDim : Theme.TextFaint, flags | TextFormatFlags.SingleLine);
        }
    }
}

/// <summary>
/// Lista rysowana ręcznie, przewijana przeciągnięciem palca (albo kółkiem / paskiem po prawej).
/// Krótkie stuknięcie bez przesunięcia = kliknięcie elementu.
/// </summary>
abstract class TouchList : Control
{
    readonly List<int> _tops = new();
    int _contentHeight;
    int _scroll;
    int _downY, _downScroll, _downIndex = -1;
    bool _down, _dragging, _scrollbarDrag;

    protected TouchList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
        TabStop = false;
        BackColor = Theme.Back;
    }

    public event Action<int>? ItemClicked;

    protected abstract int Count { get; }
    protected abstract int HeightOf(int index);
    protected abstract void PaintItem(Graphics g, int index, Rectangle bounds, bool pressed);
    protected virtual bool IsClickable(int index) => true;

    int ScrollbarWidth => LogicalToDeviceUnits(12);
    protected int ItemWidth => Math.Max(1, Width - ScrollbarWidth - LogicalToDeviceUnits(4));
    int MaxScroll => Math.Max(0, _contentHeight - Height);

    /// <summary>Przelicza pozycje elementów – wołać po zmianie liczby/wysokości elementów.</summary>
    public void RefreshItems()
    {
        _tops.Clear();
        int y = 0;
        for (int i = 0; i < Count; i++)
        {
            _tops.Add(y);
            y += HeightOf(i);
        }
        _contentHeight = y;
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        Invalidate();
    }

    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= _tops.Count) return;
        int top = _tops[index], bottom = top + HeightOf(index);
        int margin = LogicalToDeviceUnits(40);
        if (top - margin < _scroll) _scroll = top - margin;
        else if (bottom + margin > _scroll + Height) _scroll = bottom + margin - Height;
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        Invalidate();
    }

    public void ScrollToTop()
    {
        _scroll = 0;
        Invalidate();
    }

    int HitTest(int y)
    {
        y += _scroll;
        for (int i = 0; i < _tops.Count; i++)
            if (y >= _tops[i] && y < _tops[i] + HeightOf(i)) return i;
        return -1;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _down = true;
        _dragging = false;
        _downY = e.Y;
        _downScroll = _scroll;
        _scrollbarDrag = MaxScroll > 0 && e.X >= Width - ScrollbarWidth * 2;
        _downIndex = _scrollbarDrag ? -1 : HitTest(e.Y);
        if (_scrollbarDrag) ScrollbarTo(e.Y);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_down) return;
        if (_scrollbarDrag)
        {
            ScrollbarTo(e.Y);
            return;
        }
        int dy = e.Y - _downY;
        if (!_dragging && Math.Abs(dy) > LogicalToDeviceUnits(10))
        {
            _dragging = true;
            _downIndex = -1;
        }
        if (_dragging)
        {
            _scroll = Math.Clamp(_downScroll - dy, 0, MaxScroll);
            Invalidate();
        }
    }

    void ScrollbarTo(int y)
    {
        _scroll = (int)Math.Clamp((double)y / Math.Max(1, Height) * _contentHeight - Height / 2.0, 0, MaxScroll);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_down) return;
        _down = false;
        int index = _downIndex;
        _downIndex = -1;
        Invalidate();
        if (!_dragging && !_scrollbarDrag && index >= 0 && index == HitTest(e.Y) && IsClickable(index))
            ItemClicked?.Invoke(index);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _scroll = Math.Clamp(_scroll - e.Delta * LogicalToDeviceUnits(60) / 120, 0, MaxScroll);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        for (int i = 0; i < _tops.Count; i++)
        {
            int y = _tops[i] - _scroll;
            int h = HeightOf(i);
            if (y + h < 0) continue;
            if (y > Height) break;
            PaintItem(g, i, new Rectangle(0, y, ItemWidth, h), i == _downIndex);
        }

        if (MaxScroll > 0)
        {
            int w = LogicalToDeviceUnits(6);
            int thumbH = Math.Max(LogicalToDeviceUnits(40), Height * Height / Math.Max(1, _contentHeight));
            int thumbY = (int)((Height - thumbH) * (_scroll / (double)MaxScroll));
            using var track = new SolidBrush(Theme.Panel);
            using var thumb = new SolidBrush(Theme.ButtonHover);
            g.FillRectangle(track, Width - ScrollbarWidth + (ScrollbarWidth - w) / 2, 0, w, Height);
            using var path = Gfx.RoundedRect(new Rectangle(Width - ScrollbarWidth + (ScrollbarWidth - w) / 2 - 1, thumbY, w + 2, thumbH), w / 2 + 1);
            g.FillPath(thumb, path);
        }
    }
}

/// <summary>Prosta lista tekstów (profile, reguły, procesy).</summary>
sealed class SimpleList : TouchList
{
    public sealed record Item(string Text, string? Sub = null, bool Marked = false, Color? SubColor = null);

    List<Item> _items = new();
    int _selected = -1;

    protected override int Count => _items.Count;
    protected override int HeightOf(int index) => LogicalToDeviceUnits(_items[index].Sub != null ? 60 : 52);

    public int SelectedIndex
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    public void SetItems(List<Item> items)
    {
        _items = items;
        if (_selected >= items.Count) _selected = -1;
        RefreshItems();
    }

    protected override void PaintItem(Graphics g, int index, Rectangle b, bool pressed)
    {
        var item = _items[index];
        var rect = new Rectangle(b.X + 2, b.Y + 2, b.Width - 4, b.Height - 4);
        var fill = pressed ? Theme.ButtonHover : index == _selected ? Theme.RowSelected : Theme.Row;
        using (var path = Gfx.RoundedRect(rect, LogicalToDeviceUnits(6)))
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);
        if (index == _selected)
        {
            using var accent = new SolidBrush(Theme.Accent);
            g.FillRectangle(accent, rect.X, rect.Y + LogicalToDeviceUnits(6), LogicalToDeviceUnits(4), rect.Height - LogicalToDeviceUnits(12));
        }

        int pad = LogicalToDeviceUnits(12);
        var text = new Rectangle(rect.X + pad, rect.Y, rect.Width - pad * 2, rect.Height);
        var flags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        if (item.Sub == null)
        {
            TextRenderer.DrawText(g, (item.Marked ? "● " : "") + item.Text, item.Marked ? Theme.Bold : Theme.Normal, text, Theme.Text,
                flags | TextFormatFlags.VerticalCenter);
        }
        else
        {
            int h1 = TextRenderer.MeasureText("Ag", Theme.Bold).Height;
            int h2 = TextRenderer.MeasureText("Ag", Theme.Small).Height;
            int top = rect.Y + (rect.Height - h1 - h2) / 2;
            TextRenderer.DrawText(g, (item.Marked ? "● " : "") + item.Text, item.Marked ? Theme.Bold : Theme.Normal,
                new Rectangle(text.X, top, text.Width, h1), Theme.Text, flags);
            TextRenderer.DrawText(g, item.Sub, Theme.Small, new Rectangle(text.X, top + h1, text.Width, h2),
                item.SubColor ?? Theme.TextDim, flags);
        }
    }
}

/// <summary>Suwak pod palec z wartością liczbową po prawej.</summary>
sealed class TouchSlider : Control
{
    double _value;
    bool _dragging;

    public TouchSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
        TabStop = false;
        BackColor = Theme.Back;
    }

    public double Minimum { get; set; }
    public double Maximum { get; set; } = 1;
    public double Step { get; set; } = 0.01;
    public string Format { get; set; } = "0.00";

    public double Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(Math.Round(value / Step) * Step, Minimum, Maximum);
            v = Math.Round(v, 4);
            if (Math.Abs(v - _value) < 1e-9) return;
            _value = v;
            Invalidate();
        }
    }

    public event Action? ValueChanged;

    int ValueWidth => LogicalToDeviceUnits(64);
    int Pad => LogicalToDeviceUnits(16);
    Rectangle Track => new(Pad, Height / 2 - LogicalToDeviceUnits(3), Math.Max(1, Width - ValueWidth - Pad * 2), LogicalToDeviceUnits(6));

    void SetFromX(int x)
    {
        var t = Track;
        double old = _value;
        Value = Minimum + Math.Clamp((x - t.Left) / (double)t.Width, 0, 1) * (Maximum - Minimum);
        if (Math.Abs(old - _value) > 1e-9) ValueChanged?.Invoke();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left || e.X > Width - ValueWidth) return;
        _dragging = true;
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? BackColor);

        var t = Track;
        double ratio = Maximum > Minimum ? (_value - Minimum) / (Maximum - Minimum) : 0;
        int x = t.Left + (int)(t.Width * ratio);
        var accent = Enabled ? Theme.Accent : Theme.TextFaint;

        using (var bg = new SolidBrush(Theme.Button)) g.FillRectangle(bg, t);
        using (var fg = new SolidBrush(accent)) g.FillRectangle(fg, t.Left, t.Top, x - t.Left, t.Height);

        int r = LogicalToDeviceUnits(13);
        using (var thumb = new SolidBrush(Enabled ? Theme.Text : Theme.TextDim))
            g.FillEllipse(thumb, x - r, Height / 2 - r, r * 2, r * 2);
        using (var ring = new Pen(accent, LogicalToDeviceUnits(3)))
            g.DrawEllipse(ring, x - r, Height / 2 - r, r * 2, r * 2);

        TextRenderer.DrawText(g, _value.ToString(Format), Theme.Bold, new Rectangle(Width - ValueWidth, 0, ValueWidth, Height),
            Enabled ? Theme.Text : Theme.TextFaint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}
