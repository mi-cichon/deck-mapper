using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeckMapper;

enum StickMode { None, Keys, Mouse }

sealed record StickSettings(StickMode Mode, float MouseSpeed, float Curve);

/// <summary>Zwalidowana konfiguracja gotowa do użycia przez Mapper.</summary>
sealed class CompiledConfig
{
    public required Dictionary<DeckControl, BindingAction?> Bindings { get; init; }
    public required Dictionary<string, Dictionary<DeckControl, BindingAction?>> Layers { get; init; }
    public required StickSettings LeftStick { get; init; }
    public required StickSettings RightStick { get; init; }
    public float StickDeadzone { get; init; }
    public float TriggerThreshold { get; init; }
    public float LeftPadDeadzone { get; init; }
    public required LizardProfileSettings Lizard { get; init; }
}

// Surowy model pliku JSON (= format profilu). Kolejność właściwości = kolejność w zapisanym pliku.
sealed class RawConfig
{
    public float StickDeadzone { get; set; } = 0.35f;
    public float TriggerThreshold { get; set; } = 0.5f;
    public float LeftPadDeadzone { get; set; } = 0.3f;

    /// <summary>
    /// Stara opcja (0x81 co 2 s). Na Decku wyłączała też mysz lizarda – zastąpiona sekcją "lizard".
    /// Tylko odczyt do migracji; Normalize ją zeruje, więc nie trafia do zapisu.
    /// </summary>
    public bool? DisableLizardButtons { get; set; }

    public RawLizard? Lizard { get; set; }

    /// <summary>Etykieta w kole profili (maks. 6 znaków). Brak = pierwsze 6 znaków nazwy.</summary>
    public string? WheelLabel { get; set; }

    /// <summary>Ikona w kole: emoji albo nazwa pliku PNG z profiles/icons/.</summary>
    public string? WheelIcon { get; set; }

    public RawStick? LeftStick { get; set; }
    public RawStick? RightStick { get; set; }
    public Dictionary<string, string?>? Bindings { get; set; }
    public Dictionary<string, Dictionary<string, string?>>? Layers { get; set; }
}

/// <summary>Sekcja "lizard" profilu: { "filterKeys": true, "swallow": "auto" | ["Enter", "Esc", …] }.</summary>
sealed class RawLizard
{
    public bool FilterKeys { get; set; } = true;
    public RawSwallow Swallow { get; set; } = RawSwallow.Auto();
}

[JsonConverter(typeof(RawSwallowConverter))]
sealed class RawSwallow
{
    public bool IsAuto { get; set; } = true;
    public List<string> Keys { get; set; } = new();

    public static RawSwallow Auto() => new() { IsAuto = true };
    public static RawSwallow List(IEnumerable<string> keys) => new() { IsAuto = false, Keys = keys.ToList() };
}

/// <summary>"swallow": "auto" albo tablica nazw klawiszy.</summary>
sealed class RawSwallowConverter : JsonConverter<RawSwallow>
{
    public override RawSwallow Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.Equals(text?.Trim(), "auto", StringComparison.OrdinalIgnoreCase)) return RawSwallow.Auto();
                throw new JsonException($"lizard.swallow: \"{text}\" – dozwolone \"auto\" albo lista klawiszy");
            case JsonTokenType.StartArray:
                var keys = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType != JsonTokenType.String) throw new JsonException("lizard.swallow: lista musi zawierać nazwy klawiszy");
                    var key = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(key)) keys.Add(key.Trim());
                }
                return RawSwallow.List(keys);
            case JsonTokenType.Null:
                return RawSwallow.Auto();
            default:
                throw new JsonException("lizard.swallow: dozwolone \"auto\" albo lista klawiszy");
        }
    }

    public override void Write(Utf8JsonWriter writer, RawSwallow value, JsonSerializerOptions options)
    {
        if (value.IsAuto)
        {
            writer.WriteStringValue("auto");
            return;
        }
        writer.WriteStartArray();
        foreach (var key in value.Keys) writer.WriteStringValue(key);
        writer.WriteEndArray();
    }
}

sealed class RawStick
{
    public string? Mode { get; set; }
    public float MouseSpeed { get; set; } = 1200f;
    public float Curve { get; set; } = 2f;
}

static class ConfigLoader
{
    static readonly JsonSerializerOptions ReadOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // polskie znaki i "+" zostają czytelne w pliku
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Wczytuje i kompiluje plik. Zwraca null przy błędzie krytycznym.</summary>
    public static CompiledConfig? Load(string path, List<string> errors, Func<string, bool>? profileExists = null)
    {
        var raw = LoadRaw(path, errors, out _);
        return raw == null ? null : Compile(raw, errors, profileExists);
    }

    /// <summary>Wczytuje surowy profil. text = dokładna treść pliku (do wykrywania zmian).</summary>
    public static RawConfig? LoadRaw(string path, List<string> errors, out string text)
    {
        text = "";
        try
        {
            text = File.ReadAllText(path);
            return ParseRaw(text, errors);
        }
        catch (IOException e)
        {
            errors.Add($"Nie można odczytać pliku: {e.Message}");
            return null;
        }
        catch (UnauthorizedAccessException e)
        {
            errors.Add($"Brak dostępu do pliku: {e.Message}");
            return null;
        }
    }

    public static RawConfig? ParseRaw(string json, List<string> errors)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<RawConfig>(json, ReadOptions);
            if (raw == null) errors.Add("Plik jest pusty.");
            return raw;
        }
        catch (JsonException e)
        {
            errors.Add($"Błąd JSON (linia {e.LineNumber + 1}): {e.Message}");
            return null;
        }
    }

    public static CompiledConfig LoadDefault() =>
        Compile(JsonSerializer.Deserialize<RawConfig>(DefaultConfig.Json, ReadOptions)!, new List<string>());

    public static RawConfig DefaultRaw() => JsonSerializer.Deserialize<RawConfig>(DefaultConfig.Json, ReadOptions)!;

    /// <summary>Pusty profil: domyślne ustawienia, bez przypisań.</summary>
    public static RawConfig EmptyRaw()
    {
        var raw = new RawConfig { Bindings = new(), Layers = new() };
        Normalize(raw);
        return raw;
    }

    public static CompiledConfig Compile(RawConfig raw, List<string> errors, Func<string, bool>? profileExists = null)
    {
        var bindings = CompileMap(raw.Bindings, "bindings", errors);

        var layers = new Dictionary<string, Dictionary<DeckControl, BindingAction?>>(StringComparer.OrdinalIgnoreCase);
        if (raw.Layers != null)
            foreach (var (name, map) in raw.Layers)
                layers[name] = CompileMap(map, $"layers.{name}", errors);

        if (WheelLabels.Validate(raw.WheelLabel) is { } labelError) errors.Add($"wheelLabel: {labelError}");

        var allActions = bindings.Values.Concat(layers.Values.SelectMany(l => l.Values)).ToList();
        foreach (var layer in allActions.OfType<LayerAction>().Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!layers.ContainsKey(layer))
                errors.Add($"Warstwa \"{layer}\" nie istnieje w \"layers\".");

        if (profileExists != null)
            foreach (var profile in allActions.OfType<ProfileAction>().Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase))
                if (!profileExists(profile))
                    errors.Add($"Profil \"{profile}\" (akcja @Profile) nie istnieje.");

        return new CompiledConfig
        {
            Bindings = bindings,
            Layers = layers,
            LeftStick = CompileStick(raw.LeftStick, StickMode.Keys, "leftStick", errors),
            RightStick = CompileStick(raw.RightStick, StickMode.Mouse, "rightStick", errors),
            StickDeadzone = Math.Clamp(raw.StickDeadzone, 0.05f, 0.9f),
            TriggerThreshold = Math.Clamp(raw.TriggerThreshold, 0.05f, 1f),
            LeftPadDeadzone = Math.Clamp(raw.LeftPadDeadzone, 0f, 0.9f),
            Lizard = CompileLizard(raw.Lizard, errors),
        };
        // (wheelLabel/wheelIcon nie trafiają do CompiledConfig – czyta je koło z surowego profilu)
    }

    /// <summary>Brak sekcji = filtr włączony, połykaj wszystko z mapy (migracja starych profili).</summary>
    static LizardProfileSettings CompileLizard(RawLizard? raw, List<string> errors)
    {
        if (raw == null) return LizardProfileSettings.Default;
        var swallow = raw.Swallow ?? RawSwallow.Auto();
        if (swallow.IsAuto) return new LizardProfileSettings(raw.FilterKeys, true, Array.Empty<string>());

        var keys = new List<string>();
        foreach (var key in swallow.Keys)
        {
            if (LizardKeys.TryParse(key, out _)) keys.Add(key);
            else errors.Add($"lizard.swallow: nieznany klawisz \"{key}\"");
        }
        return new LizardProfileSettings(raw.FilterKeys, false, keys);
    }

    static Dictionary<DeckControl, BindingAction?> CompileMap(Dictionary<string, string?>? source, string where, List<string> errors)
    {
        var result = new Dictionary<DeckControl, BindingAction?>();
        if (source == null) return result;

        foreach (var (name, text) in source)
        {
            if (!DeckControls.TryParse(name, out var control))
            {
                errors.Add($"{where}: nieznana kontrolka \"{name}\"");
                continue;
            }
            if (!ActionParser.TryParse(text, out var action, out var error))
            {
                errors.Add($"{where}.{name}: {error}");
                continue;
            }
            result[control] = action;
        }
        return result;
    }

    static StickSettings CompileStick(RawStick? raw, StickMode fallback, string where, List<string> errors)
    {
        if (raw == null) return new StickSettings(fallback, 1200f, 2f);

        var mode = fallback;
        if (raw.Mode != null && !Enum.TryParse(raw.Mode, true, out mode))
        {
            errors.Add($"{where}.mode: \"{raw.Mode}\" – dozwolone: keys, mouse, none");
            mode = fallback;
        }
        return new StickSettings(mode, Math.Clamp(raw.MouseSpeed, 50f, 10000f), Math.Clamp(raw.Curve, 0.5f, 5f));
    }

    /// <summary>
    /// Ujednolica profil do edycji: nazwy kontrolek kanoniczne (L1 → LB), słowniki niewrażliwe na wielkość
    /// liter, brakujące sekcje uzupełnione. Nieznane klucze zostają, żeby niczego nie zgubić przy zapisie.
    /// </summary>
    public static void Normalize(RawConfig raw)
    {
        raw.Bindings = NormalizeMap(raw.Bindings);
        var layers = new Dictionary<string, Dictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);
        if (raw.Layers != null)
            foreach (var (name, map) in raw.Layers)
                if (!string.IsNullOrWhiteSpace(name))
                    layers[name.Trim()] = NormalizeMap(map);
        raw.Layers = layers;

        // migracja: stara opcja disableLizardButtons (0x81) znika, sekcja "lizard" dostaje domyślne wartości
        raw.DisableLizardButtons = null;
        raw.Lizard ??= new RawLizard();
        raw.Lizard.Swallow ??= RawSwallow.Auto();

        raw.LeftStick ??= new RawStick();
        raw.RightStick ??= new RawStick();
        raw.LeftStick.Mode = NormalizeMode(raw.LeftStick.Mode, StickMode.Keys);
        raw.RightStick.Mode = NormalizeMode(raw.RightStick.Mode, StickMode.Mouse);
    }

    static string NormalizeMode(string? mode, StickMode fallback)
    {
        if (mode == null || !Enum.TryParse(mode, true, out StickMode parsed)) parsed = fallback;
        return parsed.ToString().ToLowerInvariant();
    }

    static Dictionary<string, string?> NormalizeMap(Dictionary<string, string?>? source)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (source == null) return result;
        foreach (var (name, value) in source)
        {
            string key = DeckControls.TryParse(name, out var control) ? control.ToString() : name;
            result[key] = value;
        }
        return result;
    }

    public static RawConfig Clone(RawConfig raw)
    {
        var copy = JsonSerializer.Deserialize<RawConfig>(JsonSerializer.Serialize(raw, WriteOptions), ReadOptions)!;
        Normalize(copy);
        return copy;
    }

    /// <summary>Zapis profilu do czytelnego JSON-a (kontrolki w stałej kolejności, z krótką ściągą na górze).</summary>
    public static string Serialize(RawConfig raw)
    {
        var ordered = new RawConfig
        {
            StickDeadzone = raw.StickDeadzone,
            TriggerThreshold = raw.TriggerThreshold,
            LeftPadDeadzone = raw.LeftPadDeadzone,
            Lizard = raw.Lizard ?? new RawLizard(),
            WheelLabel = string.IsNullOrWhiteSpace(raw.WheelLabel) ? null : raw.WheelLabel.Trim(),
            WheelIcon = string.IsNullOrWhiteSpace(raw.WheelIcon) ? null : raw.WheelIcon.Trim(),
            LeftStick = raw.LeftStick,
            RightStick = raw.RightStick,
            Bindings = Ordered(raw.Bindings),
            Layers = raw.Layers?.OrderBy(l => l.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(l => l.Key, l => Ordered(l.Value)),
        };
        var json = JsonSerializer.Serialize(ordered, WriteOptions);
        return (DefaultConfig.Header + json + "\n").Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
    }

    static Dictionary<string, string?> Ordered(Dictionary<string, string?>? map)
    {
        var result = new Dictionary<string, string?>();
        if (map == null) return result;
        foreach (var (key, value) in map.OrderBy(p => DeckControls.TryParse(p.Key, out var c) ? (int)c : int.MaxValue))
            result[key] = value;
        return result;
    }

    public static void Save(string path, RawConfig raw) =>
        File.WriteAllText(path, Serialize(raw), new UTF8Encoding(false));
}
