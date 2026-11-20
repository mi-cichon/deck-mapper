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

    const int WmPointerDown = 0x0246, WmPointerUp = 0x0247, WmPointerCaptureChanged = 0x024C;

    protected override void WndProc(ref Message m)
    {
        if (OnScreenKeyboard.TryRefuseActivation(ref m)) return; // dotyk klawisza nie może aktywować klawiatury

        // Dotyk obsługujemy wprost (WM_POINTER*), a nie przez „promocję” do komunikatów myszy: gdy gra (MTA) ma
        // pierwszy plan i trzyma kursor (przechwycenie / ClipCursor), zastępczy klik myszy z dotyku trafiał do gry,
        // a nie w klawisz. WM_POINTER idzie zawsze do okna pod palcem. Obsłużony = brak promocji (brak podwójnego
        // wciśnięcia). Mysz/trackpad dalej idą przez OnMouseDown.
        switch (m.Msg)
        {
            case WmPointerDown:
                _pressed = true;
                Invalidate();
                Pressed?.Invoke(this, EventArgs.Empty);
                m.Result = IntPtr.Zero;
                return;
            case WmPointerUp:
            case WmPointerCaptureChanged:
                _pressed = false;
                Invalidate();
                m.Result = IntPtr.Zero;
                return;
        }
        base.WndProc(ref m);
    }

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
