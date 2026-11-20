using System;
using System.Collections.Generic;

namespace DeckMapper;

abstract record InputToken;
sealed record KeyToken(KeyDef Key) : InputToken;
sealed record MouseToken(MouseButton Button) : InputToken;
sealed record WheelToken(int Delta, bool Horizontal) : InputToken;

abstract record BindingAction;
/// <summary>Klawisze/przyciski myszy wciskane razem i trzymane, dopóki trzymasz przycisk Decka.</summary>
sealed record ChordAction(IReadOnlyList<InputToken> Tokens) : BindingAction;
sealed record TextAction(string Text) : BindingAction;
sealed record KeyboardAction : BindingAction;
sealed record PauseAction : BindingAction;
sealed record LayerAction(string Name) : BindingAction;
sealed record ProfileAction(string Name) : BindingAction;
sealed record NextProfileAction : BindingAction;
sealed record EditorAction : BindingAction;

static class ActionParser
{
    /// <summary>Zwraca true, gdy tekst jest poprawny. action == null oznacza "nic".</summary>
    public static bool TryParse(string? text, out BindingAction? action, out string? error)
    {
        action = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;

        if (text.StartsWith("Text:", StringComparison.OrdinalIgnoreCase))
        {
            action = new TextAction(text[5..]);
            return true;
        }

        var t = text.Trim();
        if (t.StartsWith('@'))
        {
            if (t.Equals("@Keyboard", StringComparison.OrdinalIgnoreCase)) { action = new KeyboardAction(); return true; }
            if (t.Equals("@Pause", StringComparison.OrdinalIgnoreCase)) { action = new PauseAction(); return true; }
            if (t.Equals("@NextProfile", StringComparison.OrdinalIgnoreCase)) { action = new NextProfileAction(); return true; }
            if (t.Equals("@Editor", StringComparison.OrdinalIgnoreCase)) { action = new EditorAction(); return true; }
            if (t.StartsWith("@Layer:", StringComparison.OrdinalIgnoreCase) && t.Length > 7)
            {
                action = new LayerAction(t[7..].Trim());
                return true;
            }
            if (t.StartsWith("@Profile:", StringComparison.OrdinalIgnoreCase) && t[9..].Trim().Length > 0)
            {
                action = new ProfileAction(t[9..].Trim());
                return true;
            }
            error = $"nieznana akcja specjalna \"{t}\"";
            return false;
        }

        var tokens = new List<InputToken>();
        foreach (var part in t.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryParseToken(part, out var token))
            {
                error = $"nieznany klawisz \"{part}\"";
                return false;
            }
            tokens.Add(token);
        }

        if (tokens.Count == 0)
        {
            error = "pusta akcja";
            return false;
        }

        action = new ChordAction(tokens);
        return true;
    }

    public static bool TryParseToken(string s, out InputToken token)
    {
        switch (s.ToLowerInvariant())
        {
            case "lmb": case "mouse1": token = new MouseToken(MouseButton.Left); return true;
            case "rmb": case "mouse2": token = new MouseToken(MouseButton.Right); return true;
            case "mmb": case "mouse3": token = new MouseToken(MouseButton.Middle); return true;
            case "mouse4": token = new MouseToken(MouseButton.X1); return true;
            case "mouse5": token = new MouseToken(MouseButton.X2); return true;
            case "wheelup": token = new WheelToken(120, false); return true;
            case "wheeldown": token = new WheelToken(-120, false); return true;
            case "wheelleft": token = new WheelToken(-120, true); return true;
            case "wheelright": token = new WheelToken(120, true); return true;
        }

        if (KeyNames.TryGet(s, out var key))
        {
            token = new KeyToken(key);
            return true;
        }

        token = null!;
        return false;
    }
}
