using System;
using System.Collections.Generic;
using System.Linq;

namespace DeckMapper;

/// <summary>Układ klawiszy dla pickera (w jednostkach szerokości klawisza) i czytelne nazwy akcji.</summary>
static class KeyCatalog
{
    public sealed record GridKey(string Label, string Name, float X, float Y, float W = 1f, float H = 1f);

    public const float GridWidth = 23f;
    public const float GridHeight = 6.25f;

    public static readonly IReadOnlyList<GridKey> Grid = BuildGrid();

    public static readonly (string Label, string Name)[] Mouse =
    {
        ("Lewy przycisk (LPM)", "LMB"), ("Prawy przycisk (PPM)", "RMB"), ("Środkowy (ŚPM)", "MMB"),
        ("Mysz 4 (wstecz)", "Mouse4"), ("Mysz 5 (dalej)", "Mouse5"),
        ("Kółko w górę", "WheelUp"), ("Kółko w dół", "WheelDown"),
        ("Kółko w lewo", "WheelLeft"), ("Kółko w prawo", "WheelRight"),
    };

    static List<GridKey> BuildGrid()
    {
        var keys = new List<GridKey>();
        void Row(float y, float x, params (string Label, string Name, float W)[] row)
        {
            foreach (var (label, name, w) in row)
            {
                keys.Add(new GridKey(label, name, x, y, w));
                x += w;
            }
        }
        (string, string, float) K(string label, string name, float w = 1f) => (label, name, w);
        (string, string, float)[] Chars(string chars) => chars.Select(c => K(c.ToString(), c.ToString())).ToArray();

        // F-klawisze
        keys.Add(new GridKey("Esc", "Esc", 0, 0));
        for (int i = 0; i < 12; i++)
            keys.Add(new GridKey($"F{i + 1}", $"F{i + 1}", 1.5f + i * 1.125f, 0));

        float y = 1.25f;
        Row(y, 0, new[] { K("`", "Grave") }.Concat(Chars("1234567890"))
            .Concat(new[] { K("-", "Minus"), K("=", "Equals"), K("Backspace", "Backspace", 2f) }).ToArray());
        Row(y + 1, 0, new[] { K("Tab", "Tab", 1.5f) }.Concat(Chars("QWERTYUIOP"))
            .Concat(new[] { K("[", "LBracket"), K("]", "RBracket"), K("\\", "Backslash", 1.5f) }).ToArray());
        Row(y + 2, 0, new[] { K("Caps", "CapsLock", 1.75f) }.Concat(Chars("ASDFGHJKL"))
            .Concat(new[] { K(";", "Semicolon"), K("'", "Quote"), K("Enter", "Enter", 2.25f) }).ToArray());
        Row(y + 3, 0, new[] { K("L Shift", "LShift", 2.25f) }.Concat(Chars("ZXCVBNM"))
            .Concat(new[] { K(",", "Comma"), K(".", "Period"), K("/", "Slash"), K("R Shift", "RShift", 2.75f) }).ToArray());
        Row(y + 4, 0, K("L Ctrl", "LCtrl", 1.5f), K("Win", "LWin", 1.25f), K("L Alt", "LAlt", 1.5f), K("Spacja", "Space", 5.75f),
            K("R Alt", "RAlt", 1.5f), K("Menu", "Apps", 1.25f), K("R Ctrl", "RCtrl", 2.25f));

        // nawigacja
        keys.Add(new GridKey("Scroll", "ScrollLock", 16.5f, 0));
        Row(y, 15.5f, K("Ins", "Insert"), K("Home", "Home"), K("PgUp", "PageUp"));
        Row(y + 1, 15.5f, K("Del", "Delete"), K("End", "End"), K("PgDn", "PageDown"));
        keys.Add(new GridKey("↑", "Up", 16.5f, y + 3));
        Row(y + 4, 15.5f, K("←", "Left"), K("↓", "Down"), K("→", "Right"));

        // numeryczna
        Row(y, 19f, K("Num", "NumLock"), K("/", "NumDivide"), K("*", "NumMultiply"), K("−", "NumMinus"));
        Row(y + 1, 19f, K("7", "Num7"), K("8", "Num8"), K("9", "Num9"));
        Row(y + 2, 19f, K("4", "Num4"), K("5", "Num5"), K("6", "Num6"));
        Row(y + 3, 19f, K("1", "Num1"), K("2", "Num2"), K("3", "Num3"));
        Row(y + 4, 19f, K("0", "Num0", 2f), K(",", "NumDecimal"));
        keys.Add(new GridKey("+", "NumPlus", 22f, y + 1, 1f, 2f));
        keys.Add(new GridKey("Ent", "NumEnter", 22f, y + 3, 1f, 2f));
        return keys;
    }

    // ---------- czytelne nazwy ----------

    static readonly Dictionary<string, string> Friendly = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = "Spacja", ["LShift"] = "L Shift", ["RShift"] = "R Shift", ["Shift"] = "Shift",
        ["LCtrl"] = "L Ctrl", ["RCtrl"] = "R Ctrl", ["Ctrl"] = "Ctrl", ["Control"] = "Ctrl",
        ["LAlt"] = "L Alt", ["RAlt"] = "R Alt", ["Alt"] = "Alt", ["AltGr"] = "AltGr",
        ["Up"] = "↑", ["Down"] = "↓", ["Left"] = "←", ["Right"] = "→",
        ["LMB"] = "LPM", ["RMB"] = "PPM", ["MMB"] = "ŚPM", ["Mouse1"] = "LPM", ["Mouse2"] = "PPM", ["Mouse3"] = "ŚPM",
        ["Mouse4"] = "Mysz 4", ["Mouse5"] = "Mysz 5",
        ["WheelUp"] = "Kółko ↑", ["WheelDown"] = "Kółko ↓", ["WheelLeft"] = "Kółko ←", ["WheelRight"] = "Kółko →",
        ["Grave"] = "`", ["Minus"] = "-", ["Equals"] = "=", ["LBracket"] = "[", ["RBracket"] = "]",
        ["Backslash"] = "\\", ["Semicolon"] = ";", ["Quote"] = "'", ["Comma"] = ",", ["Period"] = ".", ["Slash"] = "/",
        ["Escape"] = "Esc", ["Return"] = "Enter", ["Back"] = "Backspace", ["Apps"] = "Menu", ["LWin"] = "Win",
        ["NumDivide"] = "Num /", ["NumMultiply"] = "Num *", ["NumMinus"] = "Num −", ["NumPlus"] = "Num +",
        ["NumEnter"] = "Num Enter", ["NumDecimal"] = "Num ,",
    };

    public static string FriendlyToken(string token)
    {
        token = token.Trim();
        if (Friendly.TryGetValue(token, out var f)) return f;
        if (token.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && token.Length == 4 && char.IsDigit(token[3]))
            return "Num " + token[3];
        return token.Length == 1 ? token.ToUpperInvariant() : token;
    }

    /// <summary>Kanoniczna nazwa klawisza z siatki dla dowolnego aliasu (np. "Shift" → "LShift").</summary>
    public static string? CanonicalKey(string token)
    {
        if (!KeyNames.TryGet(token, out var def)) return null;
        foreach (var key in Grid)
            if (KeyNames.TryGet(key.Name, out var other) && other == def)
                return key.Name;
        return token.Trim();
    }

    public static string? CanonicalMouse(string token) =>
        Mouse.Select(m => m.Name).FirstOrDefault(n => string.Equals(n, token.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? token.Trim().ToLowerInvariant() switch { "mouse1" => "LMB", "mouse2" => "RMB", "mouse3" => "MMB", _ => null };

    public static bool IsNone(string? action) =>
        string.IsNullOrWhiteSpace(action) || action.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);

    /// <summary>Opis akcji po polsku, np. "Ctrl + F1", "Warstwa „alt” (trzymaj)".</summary>
    public static string Describe(string? action)
    {
        if (IsNone(action)) return "—";
        var a = action!;
        if (a.StartsWith("Text:", StringComparison.OrdinalIgnoreCase))
        {
            var text = a[5..].Replace("\r", "").Replace("\n", "⏎").Replace("\t", "⇥");
            if (text.Length > 40) text = text[..40] + "…";
            return $"Tekst: „{text}”";
        }

        if (!ActionParser.TryParse(a, out var parsed, out _)) return "⚠ " + a.Trim();
        return parsed switch
        {
            KeyboardAction => "Klawiatura ekranowa",
            PauseAction => "Wstrzymaj / wznów",
            EditorAction => "Otwórz edytor",
            ProfileWheelAction => "Koło profili (trzymaj)",
            NextProfileAction => "Następny profil",
            LayerAction l => $"Warstwa „{l.Name}” (trzymaj)",
            ProfileAction p => $"Profil „{p.Name}”",
            ChordAction => string.Join(" + ", a.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(FriendlyToken)),
            _ => a.Trim(),
        };
    }

    public static bool UsesMouse(string? action) =>
        ActionParser.TryParse(action, out var parsed, out _) && parsed is ChordAction chord &&
        chord.Tokens.Any(t => t is MouseToken or WheelToken);
}
