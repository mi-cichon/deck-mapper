using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeckMapper;

/// <summary>
/// Nakładka koła profili: okno warstwowe (WS_EX_LAYERED, alfa per piksel przez UpdateLayeredWindow), bez ramki,
/// zawsze na wierzchu, nie aktywuje się i przepuszcza kliknięcia (WS_EX_TRANSPARENT) – gra nie traci fokusu.
/// Każdy stan (brak wyboru / sektor i) renderowany raz do HBITMAP w cache; przy zmianie sektora tylko podmiana.
/// Logika wyboru żyje w wątku kontrolera (Mapper) – tu przychodzą tylko Open / SetSector / Close / Toast.
/// </summary>
sealed class WheelOverlay : Form
{
    const int WsExLayered = 0x00080000, WsExTransparent = 0x00000020, WsExNoActivate = 0x08000000;
    const int WsExToolWindow = 0x00000080, WsExTopmost = 0x00000008;
    const int UlwAlpha = 0x02;
    const byte AcSrcOver = 0x00, AcSrcAlpha = 0x01;
    const int ToastMs = 1000;

    [StructLayout(LayoutKind.Sequential)]
    struct BlendFunction { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeSize { public int Cx, Cy; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize,
        IntPtr hdcSrc, ref NativePoint pprSrc, int crKey, ref BlendFunction pblend, int dwFlags);

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    readonly string _iconsDir;
    readonly Dictionary<int, (IntPtr Bitmap, Size Size)> _cache = new(); // klucz: sektor + 1 (0 = brak wyboru)
    readonly Timer _toastTimer;
    List<WheelItem> _items = new();
    int? _sector;
    bool _open;

    public WheelOverlay(string iconsDir)
    {
        _iconsDir = iconsDir;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        _toastTimer = new Timer { Interval = ToastMs };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            if (!_open) Hide();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow | WsExTopmost;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (OnScreenKeyboard.TryRefuseActivation(ref m)) return;
        base.WndProc(ref m);
    }

    static Rectangle ScreenBounds => (Screen.PrimaryScreen ?? Screen.AllScreens[0]).Bounds;

    int Diameter => (int)(Math.Min(ScreenBounds.Width, ScreenBounds.Height) * 0.62);

    public void Open(List<WheelItem> items)
    {
        _toastTimer.Stop();
        ClearCache();
        _items = items;
        _sector = null;
        _open = true;
        Present(0);
    }

    public void SetSector(int? sector)
    {
        if (!_open || sector == _sector) return;
        _sector = sector;
        Present(sector is { } s ? s + 1 : 0);
    }

    public void CloseWheel()
    {
        _open = false;
        if (!_toastTimer.Enabled) Hide();
    }

    /// <summary>Po wyborze: nazwa profilu na środku ekranu przez ~1 s.</summary>
    public void Toast(string text)
    {
        _open = false;
        using var bmp = WheelRenderer.RenderToast(text, Math.Max(48, ScreenBounds.Height / 10));
        var hbitmap = bmp.GetHbitmap(Color.FromArgb(0));
        try { Apply(hbitmap, bmp.Size); }
        finally { DeleteObject(hbitmap); }
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    void Present(int key)
    {
        if (!_cache.TryGetValue(key, out var entry))
        {
            using var bmp = WheelRenderer.Render(Diameter, _items, key == 0 ? null : key - 1, _iconsDir);
            entry = (bmp.GetHbitmap(Color.FromArgb(0)), bmp.Size);
            _cache[key] = entry;
        }
        Apply(entry.Bitmap, entry.Size);
    }

    void Apply(IntPtr hbitmap, Size size)
    {
        var screen = ScreenBounds;
        var location = new Point(screen.Left + (screen.Width - size.Width) / 2, screen.Top + (screen.Height - size.Height) / 2);
        _ = Handle; // okno musi istnieć przed UpdateLayeredWindow

        IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc);
        IntPtr old = SelectObject(memDc, hbitmap);
        try
        {
            var dst = new NativePoint { X = location.X, Y = location.Y };
            var src = new NativePoint();
            var sz = new NativeSize { Cx = size.Width, Cy = size.Height };
            var blend = new BlendFunction { BlendOp = AcSrcOver, SourceConstantAlpha = 255, AlphaFormat = AcSrcAlpha };
            if (!UpdateLayeredWindow(Handle, screenDc, ref dst, ref sz, memDc, ref src, 0, ref blend, UlwAlpha))
                Log.Info($"Koło profili: UpdateLayeredWindow nie powiodło się (błąd {Marshal.GetLastWin32Error()})");
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }

        Bounds = new Rectangle(location, size);
        if (!Visible) Show();
    }

    void ClearCache()
    {
        foreach (var (bitmap, _) in _cache.Values) DeleteObject(bitmap);
        _cache.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toastTimer.Dispose();
        ClearCache();
        base.Dispose(disposing);
    }
}
