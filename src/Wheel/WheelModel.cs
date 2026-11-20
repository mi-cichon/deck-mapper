using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DeckMapper;

// Rdzeń koła szybkiego wyboru profilu: bez WinForms i Win32, testowany w tests/DeckMapper.Tests (także na macOS).

/// <summary>Koło profili w settings.json: 2–8 slotów, każdy = nazwa istniejącego profilu albo null (pusty).</summary>
sealed class WheelSettings
{
    public const int MinSlots = 2, MaxSlots = 8, DefaultSlots = 4;

    /// <summary>Sloty zgodnie z ruchem wskazówek zegara, slot 0 u góry.</summary>
    public List<string?> Slots { get; set; } = Enumerable.Repeat<string?>(null, DefaultSlots).ToList();

    /// <summary>Martwa strefa prawej gałki (0–1): bliżej środka = brak wyboru (puszczenie = anuluj).</summary>
    public float StickDeadzone { get; set; } = 0.5f;

    /// <summary>Martwa strefa lewego touchpada (0–1): palec przy środku = brak wyboru.</summary>
    public float PadDeadzone { get; set; } = 0.35f;

    public WheelSettings Clone() => new()
    {
        Slots = Slots.ToList(),
        StickDeadzone = StickDeadzone,
        PadDeadzone = PadDeadzone,
    };
}

/// <summary>Slot koła gotowy do narysowania. Profile == null = pusty slot.</summary>
sealed record WheelItem(string? Profile, string Label, string? Icon, bool Active)
{
    public static readonly WheelItem EmptySlot = new(null, "", null, false);
}

/// <summary>Operacje na slotach koła – wszystkie zwracają nową listę, wejście zostaje bez zmian.</summary>
static class WheelSlots
{
    /// <summary>
    /// Ujednolica sloty: liczba 2–8 (dopełnia pustymi / obcina), puste napisy = null, profile, których nie ma,
    /// → null (zgłaszane w removed), nazwy z poprawną wielkością liter.
    /// </summary>
    public static List<string?> Normalize(IEnumerable<string?>? slots, Func<string, string?> findProfile, List<string>? removed = null)
    {
        var result = new List<string?>();
        foreach (var slot in slots ?? Enumerable.Empty<string?>())
        {
            if (string.IsNullOrWhiteSpace(slot))
            {
                result.Add(null);
                continue;
            }
            var canonical = findProfile(slot.Trim());
            if (canonical == null) removed?.Add(slot.Trim());
            result.Add(canonical);
        }
        while (result.Count < WheelSettings.MinSlots) result.Add(null);
        if (result.Count > WheelSettings.MaxSlots) result = result.Take(WheelSettings.MaxSlots).ToList();
        return result;
    }

    public static List<string?> Rename(IEnumerable<string?> slots, string oldName, string newName) =>
        slots.Select(s => Same(s, oldName) ? newName : s).ToList();

    public static List<string?> Remove(IEnumerable<string?> slots, string name) =>
        slots.Select(s => Same(s, name) ? null : s).ToList();

    /// <summary>Zmiana liczby slotów; przy zmniejszaniu najpierw znikają puste sloty z końca, potem ostatnie.</summary>
    public static List<string?> Resize(IEnumerable<string?> slots, int count)
    {
        count = Math.Clamp(count, WheelSettings.MinSlots, WheelSettings.MaxSlots);
        var list = slots.ToList();
        while (list.Count < count) list.Add(null);
        for (int i = list.Count - 1; list.Count > count && i >= 0; i--)
            if (list[i] == null) list.RemoveAt(i);
        while (list.Count > count) list.RemoveAt(list.Count - 1);
        return list;
    }

    /// <summary>Przesuwa slot o delta pozycji (zamiana z sąsiadem, z zawijaniem – to koło).</summary>
    public static List<string?> Move(IEnumerable<string?> slots, int index, int delta)
    {
        var list = slots.ToList();
        if (list.Count < 2 || index < 0 || index >= list.Count) return list;
        int target = ((index + delta) % list.Count + list.Count) % list.Count;
        (list[index], list[target]) = (list[target], list[index]);
        return list;
    }

    public static List<string?> Set(IEnumerable<string?> slots, int index, string? profile)
    {
        var list = slots.ToList();
        if (index >= 0 && index < list.Count) list[index] = string.IsNullOrWhiteSpace(profile) ? null : profile;
        return list;
    }

    static bool Same(string? a, string b) => a != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Wybór sektora koła z wychylenia gałki albo pozycji palca na touchpadzie.</summary>
static class WheelSelector
{
    /// <summary>
    /// Wektor (x, y; y w górę dodatnie, jak w DeckState) → indeks sektora albo null, gdy w martwej strefie.
    /// Sektor 0 jest wyśrodkowany u góry, kolejne zgodnie z ruchem wskazówek zegara.
    /// </summary>
    public static int? Sector(float x, float y, int count, float deadzone)
    {
        if (count <= 0 || float.IsNaN(x) || float.IsNaN(y)) return null;
        float magnitude = MathF.Sqrt(x * x + y * y);
        if (magnitude < Math.Max(deadzone, 1e-4f)) return null;

        // kąt od góry, zgodnie z ruchem wskazówek zegara: atan2(x, y)
        double angle = Math.Atan2(x, y);
        if (angle < 0) angle += 2 * Math.PI;
        double step = 2 * Math.PI / count;
        return (int)Math.Floor((angle + step / 2) / step) % count;
    }

    /// <summary>
    /// Sektor z gałki (pierwszeństwo, gdy wychylona) albo z touchpada (gdy dotknięty).
    /// </summary>
    public static int? Select(float stickX, float stickY, bool padTouched, float padX, float padY, int count, WheelSettings settings)
    {
        var fromStick = Sector(stickX, stickY, count, settings.StickDeadzone);
        if (fromStick != null) return fromStick;
        return padTouched ? Sector(padX, padY, count, settings.PadDeadzone) : null;
    }

    /// <summary>Wynik puszczenia: profil z podświetlonego slotu albo null (środek, pusty slot, zły indeks).</summary>
    public static string? Result(IReadOnlyList<string?> slots, int? sector) =>
        sector is { } s && s >= 0 && s < slots.Count ? slots[s] : null;
}

/// <summary>Etykiety profili w kole.</summary>
static class WheelLabels
{
    public const int MaxLength = 6;

    /// <summary>Długość w „znakach widocznych” (emoji/znaki złożone liczone jako jeden).</summary>
    public static int Length(string? text) => string.IsNullOrEmpty(text) ? 0 : new StringInfo(text).LengthInTextElements;

    /// <summary>Pierwsze max znaków widocznych.</summary>
    public static string Truncate(string text, int max = MaxLength)
    {
        var info = new StringInfo(text);
        return info.LengthInTextElements <= max ? text : info.SubstringByTextElements(0, max);
    }

    /// <summary>wheelLabel profilu albo – gdy brak – pierwsze 6 znaków nazwy.</summary>
    public static string For(string profileName, string? wheelLabel)
    {
        var label = string.IsNullOrWhiteSpace(wheelLabel) ? profileName.Trim() : wheelLabel.Trim();
        return Truncate(label).TrimEnd();
    }

    /// <summary>Błąd walidacji etykiety albo null.</summary>
    public static string? Validate(string? label) =>
        Length(label?.Trim()) > MaxLength ? $"Etykieta może mieć najwyżej {MaxLength} znaków." : null;
}
