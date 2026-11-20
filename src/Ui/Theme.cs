using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeckMapper;

/// <summary>
/// Ciemny motyw spójny z klawiaturą ekranową. Czcionki są statyczne i nigdy nie są zwalniane –
/// dzięki temu nie da się trafić na pułapkę z setterem Control.Font (zwolniona czcionka = czerwone X).
/// </summary>
static class Theme
{
    public static readonly Color Back = Color.FromArgb(24, 24, 28);
    public static readonly Color Panel = Color.FromArgb(32, 32, 38);
    public static readonly Color Row = Color.FromArgb(40, 40, 48);
    public static readonly Color RowSelected = Color.FromArgb(44, 66, 96);
    public static readonly Color Button = Color.FromArgb(52, 52, 60);
    public static readonly Color ButtonHover = Color.FromArgb(64, 64, 74);
    public static readonly Color Checked = Color.FromArgb(60, 100, 150);
    public static readonly Color Accent = Gfx.Accent;
    public static readonly Color Text = Color.FromArgb(235, 235, 240);
    public static readonly Color TextDim = Color.FromArgb(150, 150, 162);
    public static readonly Color TextFaint = Color.FromArgb(105, 105, 115);
    public static readonly Color Override = Color.FromArgb(255, 170, 60);
    public static readonly Color Warning = Color.FromArgb(255, 200, 90);
    public static readonly Color Danger = Color.FromArgb(200, 70, 70);
    public static readonly Color Ok = Color.FromArgb(90, 190, 110);

    public static readonly Font Normal = new("Segoe UI", 11f);
    public static readonly Font Bold = new("Segoe UI", 11f, FontStyle.Bold);
    public static readonly Font Small = new("Segoe UI", 9f);
    public static readonly Font Big = new("Segoe UI", 13f);
    public static readonly Font Title = new("Segoe UI", 14f, FontStyle.Bold);
    public static readonly Font Header = new("Segoe UI", 10f, FontStyle.Bold);
    public static readonly Font Symbol = new("Segoe UI Symbol", 12f, FontStyle.Bold);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Ciemny pasek tytułu (Windows 10 20H1+ / 11).</summary>
    public static void DarkTitleBar(Form form)
    {
        try
        {
            int on = 1;
            DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int));
        }
        catch
        {
            // starszy system – zostaje jasny pasek
        }
    }

    public static Label Label(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text,
        Font = font ?? Normal,
        ForeColor = color ?? Text,
        BackColor = Color.Transparent,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        UseMnemonic = false,
    };
}
