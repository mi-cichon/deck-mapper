using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DeckMapper;

// Rdzeń funkcji „lizard + filtr klawiszy”: bez WinForms i Win32, testowany w tests/DeckMapper.Tests (także na macOS).

enum LizardOutputKind { None, Key, MouseButton }

/// <summary>Co lizard mode emituje dla jednej kontrolki: nic, klawisz albo sprzętowy przycisk myszy.</summary>
readonly record struct LizardOutput(LizardOutputKind Kind, string Name, KeyDef Key = default)
{
    public static readonly LizardOutput None = new(LizardOutputKind.None, "");

    public static LizardOutput OfKey(KeyDef key) => new(LizardOutputKind.Key, LizardKeys.NameOf(key), key);

    public static LizardOutput OfMouse(string button) => new(LizardOutputKind.MouseButton, button);

    public bool IsKey => Kind == LizardOutputKind.Key;
    public bool IsMouse => Kind == LizardOutputKind.MouseButton;

    /// <summary>Zapis w lizard-map.json: "Enter", "sc:e04D", "mouse:LMB", "none".</summary>
    public string ToText() => Kind switch
    {
        LizardOutputKind.Key => Name,
        LizardOutputKind.MouseButton => "mouse:" + Name,
        _ => "none",
    };

    public static bool TryParse(string? text, out LizardOutput output, out string? error)
    {
        output = None;
        error = null;
        var t = text?.Trim() ?? "";
        if (t.Length == 0 || t.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;

        if (t.StartsWith("mouse:", StringComparison.OrdinalIgnoreCase))
        {
            var button = LizardKeys.CanonicalMouse(t[6..]);
            if (button == null)
            {
                error = $"nieznany przycisk myszy \"{t[6..]}\" (dozwolone: LMB, RMB, MMB)";
                return false;
            }
            output = OfMouse(button);
            return true;
        }

        if (!LizardKeys.TryParse(t, out var key))
        {
            error = $"nieznany klawisz \"{t}\"";
            return false;
        }
        output = OfKey(key);
        return true;
    }
}

/// <summary>Klawisze w filtrze: identyfikacja po scancode + flaga extended (tak jak widzi je hook LL).</summary>
static class LizardKeys
{
    /// <summary>Kod do HashSetu filtra: scancode | 0x100 dla extended (np. strzałki z HID = E0 48).</summary>
    public static int Code(KeyDef key) => key.ScanCode | (key.Extended ? 0x100 : 0);

    public static int Code(uint scanCode, bool extended) => (int)(scanCode & 0xFF) | (extended ? 0x100 : 0);

    /// <summary>Nazwa z KeyNames albo "sc:XX" / "sc:e0XX" dla scancode'ów bez nazwy.</summary>
    public static string NameOf(KeyDef key) =>
        KeyNames.NameOf(key) ?? $"sc:{(key.Extended ? "e0" : "")}{key.ScanCode:X2}";

    public static bool TryParse(string text, out KeyDef key)
    {
        var t = text.Trim();
        if (t.StartsWith("sc:", StringComparison.OrdinalIgnoreCase))
        {
            var hex = t[3..];
            bool extended = hex.StartsWith("e0", StringComparison.OrdinalIgnoreCase) && hex.Length > 2;
            if (extended) hex = hex[2..];
            if (ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var sc) && sc is > 0 and < 0x100)
            {
                key = new KeyDef(sc, extended);
                return true;
            }
            key = default;
            return false;
        }
        return KeyNames.TryGet(t, out key);
    }

    public static string? CanonicalMouse(string text) => text.Trim().ToUpperInvariant() switch
    {
        "LMB" or "LEFT" or "MOUSE1" => "LMB",
        "RMB" or "RIGHT" or "MOUSE2" => "RMB",
        "MMB" or "MIDDLE" or "MOUSE3" => "MMB",
        _ => null,
    };
}

/// <summary>
/// Co lizard mode emituje dla każdej kontrolki Decka. Plik lizard-map.json powstaje w kreatorze
/// „Wykryj mapę lizarda”; bez niego używana jest wbudowana mapa oznaczona jako niezweryfikowana.
/// Kursor z prawego touchpada lizard daje zawsze – nie jest częścią mapy.
/// </summary>
sealed class LizardMap
{
    public const string FileName = "lizard-map.json";

    public LizardMap(IReadOnlyDictionary<DeckControl, LizardOutput> outputs, bool verified, DateTime? detectedAt)
    {
        Outputs = outputs;
        Verified = verified;
        DetectedAt = detectedAt;
    }

    /// <summary>Tylko kontrolki, dla których lizard coś emituje albo które sprawdzono (None = sprawdzone, nic).</summary>
    public IReadOnlyDictionary<DeckControl, LizardOutput> Outputs { get; }

    /// <summary>false = wbudowana mapa (zgadnięta z README/hid-steam.c), nie z kreatora.</summary>
    public bool Verified { get; }

    public DateTime? DetectedAt { get; }

    public LizardOutput Of(DeckControl control) => Outputs.TryGetValue(control, out var o) ? o : LizardOutput.None;

    /// <summary>Wszystkie różne klawisze emitowane przez lizarda, w stałej kolejności (kolejność kontrolek).</summary>
    public IReadOnlyList<LizardOutput> Keys =>
        Outputs.OrderBy(p => (int)p.Key).Select(p => p.Value).Where(o => o.IsKey)
            .GroupBy(o => LizardKeys.Code(o.Key)).Select(g => g.First()).ToList();

    /// <summary>
    /// Wbudowana mapa – NIEZWERYFIKOWANA. Źródło: komentarze hid-steam.c („esc, enter, cursors”, kliki myszy)
    /// i obserwacje z PoC na Decku (A = Enter, R2 = LMB, L2 = RMB, klik prawego padu = LMB).
    /// </summary>
    public static LizardMap BuiltIn { get; } = new(new Dictionary<DeckControl, LizardOutput>
    {
        [DeckControl.A] = LizardOutput.OfKey(Key("Enter")),
        [DeckControl.B] = LizardOutput.OfKey(Key("Esc")),
        [DeckControl.DpadUp] = LizardOutput.OfKey(Key("Up")),
        [DeckControl.DpadDown] = LizardOutput.OfKey(Key("Down")),
        [DeckControl.DpadLeft] = LizardOutput.OfKey(Key("Left")),
        [DeckControl.DpadRight] = LizardOutput.OfKey(Key("Right")),
        [DeckControl.RT] = LizardOutput.OfMouse("LMB"),
        [DeckControl.LT] = LizardOutput.OfMouse("RMB"),
        [DeckControl.RightPadClick] = LizardOutput.OfMouse("LMB"),
    }, verified: false, detectedAt: null);

    static KeyDef Key(string name) => KeyNames.TryGet(name, out var k) ? k : throw new InvalidOperationException(name);

    // ---------- JSON ----------

    sealed class FileModel
    {
        public bool Verified { get; set; }
        public DateTime? DetectedAt { get; set; }
        public Dictionary<string, string?>? Controls { get; set; }
    }

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>null przy błędzie krytycznym; błędne wpisy są pomijane i trafiają do errors.</summary>
    public static LizardMap? Parse(string json, List<string> errors)
    {
        FileModel? model;
        try
        {
            model = JsonSerializer.Deserialize<FileModel>(json, Options);
        }
        catch (JsonException e)
        {
            errors.Add($"{FileName}: błąd JSON (linia {e.LineNumber + 1}): {e.Message}");
            return null;
        }
        if (model?.Controls == null)
        {
            errors.Add($"{FileName}: brak sekcji \"controls\".");
            return null;
        }

        var outputs = new Dictionary<DeckControl, LizardOutput>();
        foreach (var (name, text) in model.Controls)
        {
            if (!DeckControls.TryParse(name, out var control))
            {
                errors.Add($"{FileName}: nieznana kontrolka \"{name}\"");
                continue;
            }
            if (!LizardOutput.TryParse(text, out var output, out var error))
            {
                errors.Add($"{FileName}: {name}: {error}");
                continue;
            }
            outputs[control] = output;
        }
        return new LizardMap(outputs, model.Verified, model.DetectedAt);
    }

    public string Serialize()
    {
        var model = new FileModel
        {
            Verified = Verified,
            DetectedAt = DetectedAt,
            Controls = Outputs.OrderBy(p => (int)p.Key).ToDictionary(p => p.Key.ToString(), p => (string?)p.Value.ToText()),
        };
        return JsonSerializer.Serialize(model, Options) + Environment.NewLine;
    }

    /// <summary>Wczytuje plik; gdy go nie ma albo jest zepsuty – wbudowana mapa. source opisuje, skąd jest mapa.</summary>
    public static LizardMap LoadOrBuiltIn(string path, List<string> errors, out string source)
    {
        try
        {
            if (File.Exists(path))
            {
                var map = Parse(File.ReadAllText(path), errors);
                if (map != null)
                {
                    source = $"{FileName}{(map.Verified ? "" : " (niezweryfikowana)")}";
                    return map;
                }
                source = $"wbudowana, niezweryfikowana ({FileName} ma błąd)";
                return BuiltIn;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{FileName}: nie można odczytać: {e.Message}");
        }
        source = "wbudowana, niezweryfikowana (brak " + FileName + ")";
        return BuiltIn;
    }

    public void Save(string path) => File.WriteAllText(path, Serialize(), new UTF8Encoding(false));
}
