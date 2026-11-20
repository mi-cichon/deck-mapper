using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace DeckMapper;

enum RowState { Empty, Set, Inherited, Overridden, Invalid }

/// <summary>
/// Plakietka „co robi tu lizard”: klawisz (Struck = filtr go połyka) albo sprzętowa mysz (Locked – działa zawsze).
/// </summary>
sealed record LizardBadge(string Text, bool Struck, bool Locked, string Tooltip);

/// <summary>Lista wszystkich kontrolek Decka pogrupowana, z akcją przypisaną w bazie/warstwie.</summary>
sealed class BindingListView : TouchList
{
    public sealed record Row(string Action, RowState State, string? Tag, string? Note, Color? NoteColor, LizardBadge? Badge = null);

    static readonly Font BadgeFont = new("Segoe UI", 9f);
    static readonly Font BadgeStruck = new("Segoe UI", 9f, FontStyle.Strikeout);

    readonly List<object> _items = new(); // string = nagłówek grupy, ControlInfo = wiersz
    readonly List<(Rectangle Bounds, string Text)> _badgeTips = new(); // z ostatniego rysowania
    readonly ToolTip _tip = new() { InitialDelay = 300, AutoPopDelay = 10000 };
    string? _tipText;
    DeckControl? _flash;
    long _flashUntil;

    public BindingListView()
    {
        string? group = null;
        foreach (var info in ControlInfo.All)
        {
            if (info.Group != group)
            {
                group = info.Group;
                _items.Add(group);
            }
            _items.Add(info);
        }
        ItemClicked += i =>
        {
            if (_items[i] is ControlInfo info) ControlClicked?.Invoke(info.Control);
        };
        RefreshItems();
    }

    public Func<DeckControl, Row>? RowProvider { get; set; }
    public DeckControl? Selected { get; set; }
    public event Action<DeckControl>? ControlClicked;

    protected override int Count => _items.Count;
    protected override int HeightOf(int index) => LogicalToDeviceUnits(_items[index] is string ? 38 : 62);
    protected override bool IsClickable(int index) => _items[index] is ControlInfo;

    public int IndexOf(DeckControl control) => _items.FindIndex(i => i is ControlInfo ci && ci.Control == control);

    /// <summary>Krótkie podświetlenie wiersza po wciśnięciu przycisku na Decku.</summary>
    public void Flash(DeckControl control)
    {
        _flash = control;
        _flashUntil = Environment.TickCount64 + 900;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        _badgeTips.Clear();
        base.OnPaint(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        string? text = null;
        foreach (var (bounds, tip) in _badgeTips)
            if (bounds.Contains(e.Location)) { text = tip; break; }
        if (text == _tipText) return;
        _tipText = text;
        _tip.SetToolTip(this, text ?? "");
        if (text == null) _tip.Hide(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }

    public void Tick()
    {
        if (_flash != null && Environment.TickCount64 > _flashUntil)
        {
            _flash = null;
            Invalidate();
        }
    }

    /// <summary>Plakietka lizarda wyrównana do prawej krawędzi right, wyśrodkowana w pionie na centerY.</summary>
    Rectangle PaintBadge(Graphics g, LizardBadge badge, int right, int centerY)
    {
        const TextFormatFlags Flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
        var font = badge.Struck ? BadgeStruck : BadgeFont;
        int lockW = badge.Locked ? LogicalToDeviceUnits(14) : 0;
        var size = TextRenderer.MeasureText(badge.Text, font);
        int h = size.Height + LogicalToDeviceUnits(6);
        int w = size.Width + lockW + LogicalToDeviceUnits(12);
        var r = new Rectangle(right - w, centerY - h / 2, w, h);

        var fill = badge.Locked ? Color.FromArgb(40, 70, 52) : badge.Struck ? Theme.Panel : Color.FromArgb(78, 62, 30);
        var color = badge.Locked ? Theme.Ok : badge.Struck ? Theme.TextFaint : Theme.Warning;
        using (var path = Gfx.RoundedRect(r, h / 2))
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);

        int x = r.X + LogicalToDeviceUnits(6);
        if (badge.Locked)
        {
            // kłódka: kabłąk + korpus
            int bw = LogicalToDeviceUnits(9), bh = LogicalToDeviceUnits(7);
            var body = new Rectangle(x, centerY - bh / 2 + LogicalToDeviceUnits(2), bw, bh);
            using var pen = new Pen(color, Math.Max(1f, DeviceDpi / 96f * 1.5f));
            using var brush = new SolidBrush(color);
            g.DrawArc(pen, body.X + LogicalToDeviceUnits(1), body.Y - LogicalToDeviceUnits(6), bw - LogicalToDeviceUnits(2), LogicalToDeviceUnits(10), 180, 180);
            g.FillRectangle(brush, body);
            x += lockW;
        }
        TextRenderer.DrawText(g, badge.Text, font, new Rectangle(x, r.Y, r.Right - x, r.Height), color, Flags);

        _badgeTips.Add((r, badge.Tooltip));
        return r;
    }

    protected override void PaintItem(Graphics g, int index, Rectangle b, bool pressed)
    {
        const TextFormatFlags Line = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        if (_items[index] is string header)
        {
            var r = new Rectangle(b.X + LogicalToDeviceUnits(6), b.Y, b.Width, b.Height - LogicalToDeviceUnits(4));
            TextRenderer.DrawText(g, header.ToUpperInvariant(), Theme.Header, r, Theme.TextDim, Line | TextFormatFlags.Bottom | TextFormatFlags.Left);
            return;
        }

        var info = (ControlInfo)_items[index];
        var row = RowProvider?.Invoke(info.Control) ?? new Row("—", RowState.Empty, null, null, null);
        bool selected = Selected == info.Control;
        bool flash = _flash == info.Control;

        var rect = new Rectangle(b.X + 2, b.Y + 2, b.Width - 4, b.Height - 4);
        var fill = flash ? Color.FromArgb(36, 100, 160) : pressed ? Theme.ButtonHover : selected ? Theme.RowSelected : Theme.Row;
        using (var path = Gfx.RoundedRect(rect, LogicalToDeviceUnits(6)))
        {
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
            if (selected)
                using (var pen = new Pen(Theme.Accent, LogicalToDeviceUnits(2)))
                    g.DrawPath(pen, path);
        }

        // symbol
        var badge = new Rectangle(rect.X + LogicalToDeviceUnits(10), rect.Y + (rect.Height - LogicalToDeviceUnits(40)) / 2,
            LogicalToDeviceUnits(58), LogicalToDeviceUnits(40));
        using (var path = Gfx.RoundedRect(badge, LogicalToDeviceUnits(8)))
        using (var brush = new SolidBrush(flash ? Theme.Accent : Theme.Button))
            g.FillPath(brush, path);
        TextRenderer.DrawText(g, info.Symbol, Theme.Symbol, badge, Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        int pad = LogicalToDeviceUnits(12);
        int actionW = Math.Min((int)(rect.Width * 0.45f), LogicalToDeviceUnits(440));
        int nameX = badge.Right + pad;
        var nameRect = new Rectangle(nameX, rect.Y, rect.Right - actionW - pad * 2 - nameX, rect.Height);

        if (row.Badge is { } lizard)
        {
            var pill = PaintBadge(g, lizard, nameRect.Right, rect.Y + rect.Height / 2);
            nameRect.Width = Math.Max(0, pill.Left - pad / 2 - nameRect.X);
        }
        var actionRect = new Rectangle(rect.Right - actionW - pad, rect.Y, actionW, rect.Height);

        int h1 = TextRenderer.MeasureText("Ag", Theme.Bold).Height;
        int h2 = TextRenderer.MeasureText("Ag", Theme.Small).Height;
        int top = rect.Y + (rect.Height - h1 - h2) / 2;

        if (row.Note == null)
            TextRenderer.DrawText(g, info.Name, Theme.Bold, nameRect, Theme.Text, Line | TextFormatFlags.VerticalCenter);
        else
        {
            TextRenderer.DrawText(g, info.Name, Theme.Bold, new Rectangle(nameRect.X, top, nameRect.Width, h1), Theme.Text, Line);
            TextRenderer.DrawText(g, row.Note, Theme.Small, new Rectangle(nameRect.X, top + h1, nameRect.Width, h2),
                row.NoteColor ?? Theme.TextDim, Line);
        }

        var (actionColor, actionFont) = row.State switch
        {
            RowState.Empty => (Theme.TextFaint, Theme.Normal),
            RowState.Inherited => (Theme.TextDim, Theme.Normal),
            RowState.Overridden => (Theme.Override, Theme.Bold),
            RowState.Invalid => (Theme.Warning, Theme.Bold),
            _ => (Theme.Text, Theme.Bold),
        };
        var flags = Line | TextFormatFlags.Right;

        if (row.Tag == null)
            TextRenderer.DrawText(g, row.Action, actionFont, actionRect, actionColor, flags | TextFormatFlags.VerticalCenter);
        else
        {
            TextRenderer.DrawText(g, row.Action, actionFont, new Rectangle(actionRect.X, top, actionRect.Width, h1), actionColor, flags);
            TextRenderer.DrawText(g, row.Tag, Theme.Small, new Rectangle(actionRect.X, top + h1, actionRect.Width, h2),
                row.State == RowState.Overridden ? Theme.Override : Theme.TextFaint, flags);
        }
    }
}

/// <summary>Pasek na dole edytora: co jest teraz wciśnięte na Decku i co by to wysłało.</summary>
sealed class PreviewBar : Control
{
    string _prefix = "";
    List<string> _chips = new();
    string _key = "";

    public PreviewBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Theme.Panel;
    }

    public void Set(string prefix, List<string> chips)
    {
        var key = prefix + "\u0001" + string.Join("\u0001", chips);
        if (key == _key) return;
        _key = key;
        _prefix = prefix;
        _chips = chips;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        const TextFormatFlags Flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        int pad = LogicalToDeviceUnits(12);
        int x = pad;
        var prefixSize = TextRenderer.MeasureText(_prefix, Theme.Small);
        TextRenderer.DrawText(g, _prefix, Theme.Small, new Rectangle(x, 0, prefixSize.Width, Height), Theme.TextDim, Flags);
        x += prefixSize.Width + pad;

        int chipH = Height - LogicalToDeviceUnits(14);
        using var brush = new SolidBrush(Theme.Checked);
        foreach (var chip in _chips)
        {
            int w = TextRenderer.MeasureText(chip, Theme.Normal).Width + LogicalToDeviceUnits(20);
            if (x + w > Width - pad)
            {
                TextRenderer.DrawText(g, "…", Theme.Bold, new Rectangle(x, 0, LogicalToDeviceUnits(30), Height), Theme.Text, Flags);
                break;
            }
            var r = new Rectangle(x, (Height - chipH) / 2, w, chipH);
            using (var path = Gfx.RoundedRect(r, chipH / 2)) g.FillPath(brush, path);
            TextRenderer.DrawText(g, chip, Theme.Normal, r, Theme.Text, Flags | TextFormatFlags.HorizontalCenter);
            x += w + LogicalToDeviceUnits(8);
        }
    }
}

/// <summary>Panel, który dzieli szerokość po równo między dzieci (np. przyciski trybu gałki).</summary>
sealed class RowPanel : Panel
{
    public RowPanel()
    {
        BackColor = Theme.Back;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int n = Controls.Count;
        if (n == 0) return;
        int gap = LogicalToDeviceUnits(6);
        int w = (Width - gap * (n - 1)) / n;
        for (int i = 0; i < n; i++)
            Controls[i].Bounds = new Rectangle(i * (w + gap), 0, w, Height);
    }
}

/// <summary>
/// Podgląd koła profili w edytorze. Rysuje tym samym WheelRendererem co nakładka w grze (bitmapa w cache,
/// przerysowanie tylko przy zmianie danych/sektora). Stuknięcie w sektor = wybór slotu (ta sama geometria co w grze).
/// LiveSector = sektor wskazywany teraz prawą gałką / lewym touchpadem (ma pierwszeństwo przed zaznaczeniem).
/// </summary>
sealed class WheelPreview : Control
{
    readonly string _iconsDir;
    List<WheelItem> _items = new();
    int _selected;
    int? _live;
    Bitmap? _cache;
    string _cacheKey = "";

    public WheelPreview(string iconsDir)
    {
        _iconsDir = iconsDir;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Theme.Back;
    }

    public event Action<int>? SlotClicked;

    public void SetItems(List<WheelItem> items, int selected)
    {
        _items = items;
        _selected = selected;
        Invalidate();
    }

    public int? LiveSector
    {
        get => _live;
        set
        {
            if (_live == value) return;
            _live = value;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int d = Math.Min(Width, Height);
        float c = d / 2f;
        // y ekranu rośnie w dół, WheelSelector oczekuje y w górę
        var sector = WheelSelector.Sector((e.X - c) / c, (c - e.Y) / c, _items.Count, 0.3f);
        if (sector is { } s) SlotClicked?.Invoke(s);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        int d = Math.Min(Width, Height);
        if (d < 40 || _items.Count == 0) return;

        int? highlight = _live ?? _selected;
        var key = $"{d}|{highlight}|{string.Join("|", _items)}";
        if (_cache == null || key != _cacheKey)
        {
            _cache?.Dispose();
            _cache = WheelRenderer.Render(d, _items, highlight, _iconsDir);
            _cacheKey = key;
        }
        e.Graphics.DrawImageUnscaled(_cache, 0, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cache?.Dispose();
        base.Dispose(disposing);
    }
}
