using System;
using System.Collections.Generic;

namespace DeckMapper;

/// <summary>
/// Wszystkie kontrolki, które da się zmapować. Nazwy enumów = nazwy w mapping.json.
/// Kontrolki "wirtualne" (kierunki gałek i lewego touchpada, LT/RT) wylicza Mapper.
/// </summary>
enum DeckControl
{
    A, B, X, Y,
    LB, RB, LT, RT,
    L3, R3,
    L4, R4, L5, R5,
    View, Menu, Steam, QuickAccess,
    DpadUp, DpadDown, DpadLeft, DpadRight,
    LeftPadClick, RightPadClick,

    // wirtualne
    LeftPadUp, LeftPadDown, LeftPadLeft, LeftPadRight,
    LeftStickUp, LeftStickDown, LeftStickLeft, LeftStickRight,
    RightStickUp, RightStickDown, RightStickLeft, RightStickRight,
}

static class DeckControls
{
    public static readonly int Count = Enum.GetValues<DeckControl>().Length;

    static readonly Dictionary<string, DeckControl> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["L1"] = DeckControl.LB,
        ["R1"] = DeckControl.RB,
        ["L2"] = DeckControl.LT,
        ["R2"] = DeckControl.RT,
        ["Select"] = DeckControl.View,
        ["Start"] = DeckControl.Menu,
        ["..."] = DeckControl.QuickAccess,
    };

    public static bool TryParse(string? name, out DeckControl control)
    {
        control = default;
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();
        if (Aliases.TryGetValue(name, out control)) return true;
        if (char.IsDigit(name[0]) || name[0] == '-') return false;
        return Enum.TryParse(name, true, out control) && Enum.IsDefined(control);
    }
}
