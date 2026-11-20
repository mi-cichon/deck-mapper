using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace DeckMapper;

/// <summary>
/// Rysuje koło profili do bitmapy ARGB (nakładka w grze i podgląd w edytorze). Geometria zgodna z WheelSelector:
/// sektor 0 wyśrodkowany u góry, kolejne zgodnie z ruchem wskazówek zegara.
/// </summary>
static class WheelRenderer
{
    static readonly Color Ring = Color.FromArgb(215, 44, 44, 52);
    static readonly Color RingEmpty = Color.FromArgb(150, 34, 34, 40);
    static readonly Color Back = Color.FromArgb(150, 16, 16, 20);
    static readonly Dictionary<string, Image?> Icons = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap Render(int diameter, IReadOnlyList<WheelItem> items, int? sector, string iconsDir)
    {
        var bmp = new Bitmap(diameter, diameter, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        float c = diameter / 2f;
        float outer = c - 4, inner = outer * 0.36f;
        using (var back = new SolidBrush(Back)) g.FillEllipse(back, c - outer - 2, c - outer - 2, (outer + 2) * 2, (outer + 2) * 2);

        int n = Math.Max(1, items.Count);
        float step = 360f / n, gap = n > 1 ? 1.6f : 0f;
        float ringMid = (outer + inner) / 2f, thickness = outer - inner;
        using var labelFont = new Font("Segoe UI", Math.Max(9f, thickness * 0.17f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var emojiFont = new Font("Segoe UI Emoji", Math.Max(10f, thickness * 0.26f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            bool hot = sector == i;
            float start = -90f + i * step - step / 2 + gap / 2, sweep = step - gap;

            using (var path = Segment(c, inner, outer, start, sweep))
            {
                var fill = hot ? Color.FromArgb(240, Gfx.Accent) : item.Profile == null ? RingEmpty : Ring;
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                if (item.Active)
                    using (var pen = new Pen(Theme.Ok, Math.Max(2f, thickness * 0.05f))) g.DrawPath(pen, path);
            }

            // treść slotu na środku pierścienia
            double mid = (start + sweep / 2) * Math.PI / 180;
            float x = c + (float)Math.Cos(mid) * ringMid, y = c + (float)Math.Sin(mid) * ringMid;
            float box = Math.Min(thickness * 0.95f, (float)(2 * Math.PI * ringMid / n) * 0.9f);
            var textColor = item.Profile == null ? Color.FromArgb(150, 150, 150, 160) : Color.White;

            if (item.Profile == null)
            {
                using var dim = new SolidBrush(textColor);
                g.DrawString("—", labelFont, dim, new RectangleF(x - box / 2, y - box / 2, box, box), centered);
                continue;
            }

            bool hasIcon = DrawIcon(g, item.Icon, iconsDir, emojiFont, x, y - box * 0.18f, box * 0.42f, centered);
            using (var text = new SolidBrush(textColor))
                g.DrawString(item.Label, labelFont, text,
                    new RectangleF(x - box / 2, hasIcon ? y + box * 0.04f : y - box / 4, box, box / 2), centered);

            if (item.Active)
                using (var ok = new SolidBrush(Theme.Ok))
                    g.FillEllipse(ok, x - 4, y + box * 0.36f, 8, 8);
        }

        // środek: co się stanie po puszczeniu
        using (var hub = new SolidBrush(Color.FromArgb(230, 24, 24, 28))) g.FillEllipse(hub, c - inner + 6, c - inner + 6, (inner - 6) * 2, (inner - 6) * 2);
        string hint;
        Color hintColor;
        if (items.TrueForAllProfilesEmpty())
            (hint, hintColor) = ("Koło puste –\nustaw w edytorze", Color.FromArgb(200, Theme.Warning));
        else if (sector is { } s && s < items.Count && items[s].Profile is { } p)
            (hint, hintColor) = (p, Color.White);
        else if (sector != null)
            (hint, hintColor) = ("pusty slot\n(anuluj)", Color.FromArgb(180, 180, 180, 190));
        else
            (hint, hintColor) = ("puść = anuluj", Color.FromArgb(180, 180, 180, 190));
        using (var hubFont = new Font("Segoe UI", Math.Max(9f, inner * 0.2f), FontStyle.Bold, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(hintColor))
            g.DrawString(hint, hubFont, brush, new RectangleF(c - inner + 10, c - inner + 10, (inner - 10) * 2, (inner - 10) * 2), centered);

        return bmp;
    }

    /// <summary>Napis „wybrano profil” na środku ekranu.</summary>
    public static Bitmap RenderToast(string text, int height)
    {
        using var font = new Font("Segoe UI", height * 0.38f, FontStyle.Bold, GraphicsUnit.Pixel);
        Size size;
        using (var probe = new Bitmap(1, 1))
        using (var pg = Graphics.FromImage(probe))
            size = Size.Ceiling(pg.MeasureString(text, font));
        int w = size.Width + height, h = height;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        using (var path = Gfx.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), h / 2))
        using (var brush = new SolidBrush(Color.FromArgb(225, 24, 24, 28)))
        using (var pen = new Pen(Gfx.Accent, 3))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, Brushes.White, new RectangleF(0, 0, w, h), centered);
        return bmp;
    }

    static bool TrueForAllProfilesEmpty(this IReadOnlyList<WheelItem> items)
    {
        foreach (var i in items)
            if (i.Profile != null) return false;
        return true;
    }

    static GraphicsPath Segment(float c, float inner, float outer, float start, float sweep)
    {
        var path = new GraphicsPath();
        path.AddArc(c - outer, c - outer, outer * 2, outer * 2, start, sweep);
        path.AddArc(c - inner, c - inner, inner * 2, inner * 2, start + sweep, -sweep);
        path.CloseFigure();
        return path;
    }

    /// <summary>PNG z profiles/icons/ (tylko nazwa pliku) albo emoji/tekst. true = coś narysowano.</summary>
    static bool DrawIcon(Graphics g, string? icon, string iconsDir, Font emojiFont, float x, float y, float size, StringFormat centered)
    {
        if (string.IsNullOrWhiteSpace(icon)) return false;
        if (icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            var image = LoadIcon(Path.Combine(iconsDir, Path.GetFileName(icon)));
            if (image == null) return false;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, x - size / 2, y - size / 2, size, size);
            return true;
        }
        g.DrawString(icon.Trim(), emojiFont, Brushes.White, new RectangleF(x - size, y - size / 2, size * 2, size), centered);
        return true;
    }

    static Image? LoadIcon(string path)
    {
        lock (Icons)
        {
            if (Icons.TryGetValue(path, out var cached)) return cached;
            Image? image = null;
            try
            {
                if (File.Exists(path))
                {
                    // kopia w pamięci – plik nie zostaje zablokowany
                    using var stream = new MemoryStream(File.ReadAllBytes(path));
                    using var loaded = Image.FromStream(stream);
                    image = new Bitmap(loaded);
                }
            }
            catch (Exception e)
            {
                Log.Info($"Koło profili: nie można wczytać ikony {path}: {e.Message}");
            }
            Icons[path] = image;
            return image;
        }
    }

    /// <summary>Po zmianie plików ikon (np. nowy PNG) – wczytaj od nowa.</summary>
    public static void ClearIconCache()
    {
        lock (Icons)
        {
            foreach (var image in Icons.Values) image?.Dispose();
            Icons.Clear();
        }
    }
}
