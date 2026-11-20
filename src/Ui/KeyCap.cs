using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeckMapper;

/// <summary>Klawisz klawiatury ekranowej. Nie przyjmuje fokusu, żeby gra go nie traciła.</summary>
sealed class KeyCap : Control
{
    static readonly Color Normal = Color.FromArgb(52, 52, 60);
    static readonly Color ActiveColor = Color.FromArgb(60, 100, 150);

    bool _pressed;
    bool _active;

    public KeyCap()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        ForeColor = Color.White;
    }

    public event EventHandler? Pressed;

    /// <summary>Podświetlenie, np. włączony Shift.</summary>
    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _pressed = true;
        Invalidate();
        Pressed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _pressed = false;
        Invalidate();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? BackColor);

        var rect = new Rectangle(3, 3, Math.Max(1, Width - 7), Math.Max(1, Height - 7));
        var fill = _pressed ? Gfx.Accent : _active ? ActiveColor : Normal;

        using (var path = Gfx.RoundedRect(rect, Math.Min(10, rect.Height / 4)))
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);

        TextRenderer.DrawText(g, Text, Font, rect, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}
