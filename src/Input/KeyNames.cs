using System;
using System.Collections.Generic;

namespace DeckMapper;

/// <summary>Klawisz jako scancode (Set 1) – DirectInput w grach czyta właśnie scancode'y.</summary>
readonly record struct KeyDef(ushort ScanCode, bool Extended);

static class KeyNames
{
    // kolejność rejestracji nazw – MUSI być zainicjalizowana przed Map (Build() ją wypełnia)
    static readonly List<(string Name, KeyDef Key)> Order = new();
    static readonly Dictionary<string, KeyDef> Map = Build();
    static readonly Dictionary<KeyDef, string> Names = BuildNames();

    public static readonly KeyDef LeftShift = Map["LShift"];

    public static bool TryGet(string name, out KeyDef key) => Map.TryGetValue(name.Trim(), out key);

    /// <summary>Kanoniczna (pierwsza zarejestrowana) nazwa klawisza albo null, gdy scancode nie ma nazwy.</summary>
    public static string? NameOf(KeyDef key) => Names.TryGetValue(key, out var name) ? name : null;

    static Dictionary<KeyDef, string> BuildNames()
    {
        // kolejność jak w Build(): pierwsza nazwa danego scancode'u wygrywa ("Esc", nie "Escape")
        var names = new Dictionary<KeyDef, string>();
        foreach (var (name, key) in Order) names.TryAdd(key, name);
        return names;
    }

    static Dictionary<string, KeyDef> Build()
    {
        var m = new Dictionary<string, KeyDef>(StringComparer.OrdinalIgnoreCase);
        void Add(string n, KeyDef k) { m[n] = k; Order.Add((n, k)); }
        void N(int sc, params string[] names) { foreach (var n in names) Add(n, new KeyDef((ushort)sc, false)); }
        void E(int sc, params string[] names) { foreach (var n in names) Add(n, new KeyDef((ushort)sc, true)); }
        void Row(int first, string keys) { for (int i = 0; i < keys.Length; i++) N(first + i, keys[i].ToString()); }

        N(0x01, "Esc", "Escape");
        Row(0x02, "1234567890");
        N(0x0C, "-", "Minus");
        N(0x0D, "=", "Equals");
        N(0x0E, "Backspace", "Back");
        N(0x0F, "Tab");
        Row(0x10, "QWERTYUIOP");
        N(0x1A, "[", "LBracket");
        N(0x1B, "]", "RBracket");
        N(0x1C, "Enter", "Return");
        N(0x1D, "LCtrl", "Ctrl", "Control");
        Row(0x1E, "ASDFGHJKL");
        N(0x27, ";", "Semicolon");
        N(0x28, "'", "Quote", "Apostrophe");
        N(0x29, "`", "Grave", "Tilde");
        N(0x2A, "LShift", "Shift");
        N(0x2B, "\\", "Backslash");
        Row(0x2C, "ZXCVBNM");
        N(0x33, ",", "Comma");
        N(0x34, ".", "Period", "Dot");
        N(0x35, "/", "Slash");
        N(0x36, "RShift");
        N(0x37, "NumMultiply", "Num*");
        N(0x38, "LAlt", "Alt");
        N(0x39, "Space");
        N(0x3A, "CapsLock", "Caps");
        for (int i = 0; i < 10; i++) N(0x3B + i, $"F{i + 1}");
        N(0x45, "NumLock");
        N(0x46, "ScrollLock");
        N(0x47, "Num7");
        N(0x48, "Num8");
        N(0x49, "Num9");
        N(0x4A, "NumMinus", "Num-");
        N(0x4B, "Num4");
        N(0x4C, "Num5");
        N(0x4D, "Num6");
        N(0x4E, "NumPlus");
        N(0x4F, "Num1");
        N(0x50, "Num2");
        N(0x51, "Num3");
        N(0x52, "Num0");
        N(0x53, "NumDecimal", "Num.");
        N(0x57, "F11");
        N(0x58, "F12");

        E(0x1C, "NumEnter");
        E(0x1D, "RCtrl");
        E(0x35, "NumDivide", "Num/");
        E(0x38, "RAlt", "AltGr");
        E(0x47, "Home");
        E(0x48, "Up");
        E(0x49, "PageUp", "PgUp");
        E(0x4B, "Left");
        E(0x4D, "Right");
        E(0x4F, "End");
        E(0x50, "Down");
        E(0x51, "PageDown", "PgDn");
        E(0x52, "Insert", "Ins");
        E(0x53, "Delete", "Del");
        E(0x5B, "LWin", "Win");
        E(0x5C, "RWin");
        E(0x5D, "Apps");
        return m;
    }
}
