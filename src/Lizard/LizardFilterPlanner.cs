using System;
using System.Collections.Generic;
using System.Linq;

namespace DeckMapper;

/// <summary>Ustawienia lizarda z profilu (sekcja "lizard"). SwallowKeys ma znaczenie tylko, gdy !SwallowAuto.</summary>
sealed record LizardProfileSettings(bool FilterKeys, bool SwallowAuto, IReadOnlyList<string> SwallowKeys)
{
    public static readonly LizardProfileSettings Default = new(true, true, Array.Empty<string>());
}

/// <summary>Wynik planowania: czy hook ma działać i które klawisze (scancode | 0x100 extended) połykać.</summary>
sealed class SwallowPlan
{
    public required bool Active { get; init; }
    public required IReadOnlySet<int> Codes { get; init; }
    /// <summary>Nazwy połykanych klawiszy (do UI i logu), w kolejności mapy / listy z profilu.</summary>
    public required IReadOnlyList<string> KeyNames { get; init; }
    /// <summary>Np. klawisz z listy profilu, którego nie ma w mapie lizarda.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
    /// <summary>Czytelny opis stanu, np. „wyłączony w menu” – do tooltipa i logu.</summary>
    public required string Reason { get; init; }

    public bool Swallows(KeyDef key) => Active && Codes.Contains(LizardKeys.Code(key));
}

/// <summary>Co lizard robi z daną kontrolką i czy filtr to połyka – do plakietek w edytorze i ostrzeżeń w pickerze.</summary>
readonly record struct LizardControlStatus(LizardOutput Output, bool Swallowed)
{
    /// <summary>Lizard wysyła klawisz, którego filtr nie połyka – zadziała razem z mapowaniem.</summary>
    public bool KeyLeaks => Output.IsKey && !Swallowed;
}

static class LizardFilterPlanner
{
    public static SwallowPlan Plan(LizardMap map, LizardProfileSettings profile, bool globalEnabled)
    {
        if (!globalEnabled) return Inactive("wyłączony w menu zasobnika");
        if (!profile.FilterKeys) return Inactive("profil: lizard bez filtra");

        var codes = new HashSet<int>();
        var names = new List<string>();
        var warnings = new List<string>();
        var mapCodes = map.Keys.Select(k => LizardKeys.Code(k.Key)).ToHashSet();

        void Add(KeyDef key)
        {
            if (codes.Add(LizardKeys.Code(key))) names.Add(LizardKeys.NameOf(key));
        }

        if (profile.SwallowAuto)
        {
            foreach (var output in map.Keys) Add(output.Key);
        }
        else
        {
            foreach (var name in profile.SwallowKeys)
            {
                if (!LizardKeys.TryParse(name, out var key))
                {
                    warnings.Add($"nieznany klawisz „{name}” – pominięty");
                    continue;
                }
                if (!mapCodes.Contains(LizardKeys.Code(key)))
                    warnings.Add($"„{name}” nie występuje w mapie lizarda – połykany mimo to (też z klawiatury USB)");
                Add(key);
            }
        }

        return new SwallowPlan
        {
            Active = codes.Count > 0,
            Codes = codes,
            KeyNames = names,
            Warnings = warnings,
            Reason = codes.Count > 0 ? $"połyka: {string.Join(", ", names)}" : "lista klawiszy do połykania jest pusta",
        };
    }

    static SwallowPlan Inactive(string reason) => new()
    {
        Active = false,
        Codes = new HashSet<int>(),
        KeyNames = Array.Empty<string>(),
        Warnings = Array.Empty<string>(),
        Reason = reason,
    };

    public static LizardControlStatus StatusOf(DeckControl control, LizardMap map, SwallowPlan plan)
    {
        var output = map.Of(control);
        return new LizardControlStatus(output, output.IsKey && plan.Swallows(output.Key));
    }

    /// <summary>
    /// Przełączenie jednego klawisza na liście połykanych. Z "auto" robi jawną listę (wszystkie z mapy minus/plus ten).
    /// Gdy wynik równa się pełnej mapie – wraca do "auto", żeby nowe klawisze z ponownego wykrycia też były połykane.
    /// </summary>
    public static LizardProfileSettings Toggle(LizardProfileSettings settings, LizardMap map, string keyName)
    {
        var all = map.Keys.Select(k => k.Name).ToList();
        var current = settings.SwallowAuto ? all : settings.SwallowKeys.ToList();
        bool present = current.Any(n => SameKey(n, keyName));
        var next = present
            ? current.Where(n => !SameKey(n, keyName)).ToList()
            : current.Append(keyName).ToList();

        bool equalsAll = next.Count == all.Count && all.All(a => next.Any(n => SameKey(n, a)));
        return equalsAll
            ? settings with { SwallowAuto = true, SwallowKeys = Array.Empty<string>() }
            : settings with { SwallowAuto = false, SwallowKeys = next };
    }

    public static bool SameKey(string a, string b) =>
        LizardKeys.TryParse(a, out var ka) && LizardKeys.TryParse(b, out var kb)
            ? LizardKeys.Code(ka) == LizardKeys.Code(kb)
            : string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Rozpoznawanie fizycznych klawiatur po ścieżce urządzenia Raw Input (do ostrzeżenia o klawiaturze USB).</summary>
static class KeyboardDevices
{
    const string ValveVid = "VID_28DE";

    /// <summary>
    /// true = prawdziwa klawiatura USB/Bluetooth (HID z VID, ale nie Valve – klawiatura lizarda Decka ma VID_28DE).
    /// Wirtualne (RDP, Root#…) i bez VID pomijamy, żeby nie straszyć dymkiem bez powodu.
    /// </summary>
    public static bool IsExternalKeyboard(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath)) return false;
        var p = devicePath.ToUpperInvariant();
        if (p.Contains("RDP_KBD") || p.Contains(@"\ROOT#") || p.Contains("#ROOT#")) return false;
        if (p.Contains(ValveVid)) return false;
        return p.Contains("VID_") || p.Contains("VID&");
    }
}
