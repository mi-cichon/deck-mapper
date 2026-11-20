using System.Collections.Generic;
using System.Linq;

namespace DeckMapper;

/// <summary>Czytelne nazwy, symbole i grupy kontrolek Decka do edytora.</summary>
sealed record ControlInfo(DeckControl Control, string Symbol, string Name, string Group, string? Note = null)
{
    /// <summary>R2/L2 i klik prawego padu w lizard mode klikają myszą – do wyboru trzeba je przytrzymać.</summary>
    public bool LongPressToSelect => Control is DeckControl.LT or DeckControl.RT or DeckControl.RightPadClick;

    public bool IsLeftStickDirection => Control is DeckControl.LeftStickUp or DeckControl.LeftStickDown
        or DeckControl.LeftStickLeft or DeckControl.LeftStickRight;

    public bool IsRightStickDirection => Control is DeckControl.RightStickUp or DeckControl.RightStickDown
        or DeckControl.RightStickLeft or DeckControl.RightStickRight;

    public static readonly IReadOnlyList<ControlInfo> All = new ControlInfo[]
    {
        new(DeckControl.A, "A", "A", "Przyciski", "dolny"),
        new(DeckControl.B, "B", "B", "Przyciski", "prawy"),
        new(DeckControl.X, "X", "X", "Przyciski", "lewy"),
        new(DeckControl.Y, "Y", "Y", "Przyciski", "górny"),

        new(DeckControl.LB, "LB", "LB (lewy bumper)", "Bumpery / triggery", "L1"),
        new(DeckControl.RB, "RB", "RB (prawy bumper)", "Bumpery / triggery", "R1"),
        new(DeckControl.LT, "L2", "L2 / LT (lewy trigger)", "Bumpery / triggery", "lizard mode: prawy klik myszy – do wyboru przytrzymaj"),
        new(DeckControl.RT, "R2", "R2 / RT (prawy trigger)", "Bumpery / triggery", "lizard mode: lewy klik myszy – do wyboru przytrzymaj"),

        new(DeckControl.L4, "L4", "L4 – tył, lewy górny", "Tylne"),
        new(DeckControl.L5, "L5", "L5 – tył, lewy dolny", "Tylne"),
        new(DeckControl.R4, "R4", "R4 – tył, prawy górny", "Tylne"),
        new(DeckControl.R5, "R5", "R5 – tył, prawy dolny", "Tylne"),

        new(DeckControl.DpadUp, "↑", "D-pad ↑ (góra)", "D-pad"),
        new(DeckControl.DpadDown, "↓", "D-pad ↓ (dół)", "D-pad"),
        new(DeckControl.DpadLeft, "←", "D-pad ← (lewo)", "D-pad"),
        new(DeckControl.DpadRight, "→", "D-pad → (prawo)", "D-pad"),

        new(DeckControl.L3, "L3", "L3 – wciśnięcie lewej gałki", "Gałki"),
        new(DeckControl.R3, "R3", "R3 – wciśnięcie prawej gałki", "Gałki"),
        new(DeckControl.LeftStickUp, "L↑", "Lewa gałka ↑", "Gałki"),
        new(DeckControl.LeftStickDown, "L↓", "Lewa gałka ↓", "Gałki"),
        new(DeckControl.LeftStickLeft, "L←", "Lewa gałka ←", "Gałki"),
        new(DeckControl.LeftStickRight, "L→", "Lewa gałka →", "Gałki"),
        new(DeckControl.RightStickUp, "R↑", "Prawa gałka ↑", "Gałki"),
        new(DeckControl.RightStickDown, "R↓", "Prawa gałka ↓", "Gałki"),
        new(DeckControl.RightStickLeft, "R←", "Prawa gałka ←", "Gałki"),
        new(DeckControl.RightStickRight, "R→", "Prawa gałka →", "Gałki"),

        new(DeckControl.LeftPadClick, "◉", "Lewy touchpad – klik", "Lewy touchpad", "klik na środku (poza kierunkami)"),
        new(DeckControl.LeftPadUp, "◉↑", "Lewy touchpad ↑", "Lewy touchpad", "klik u góry"),
        new(DeckControl.LeftPadDown, "◉↓", "Lewy touchpad ↓", "Lewy touchpad", "klik u dołu"),
        new(DeckControl.LeftPadLeft, "◉←", "Lewy touchpad ←", "Lewy touchpad", "klik z lewej"),
        new(DeckControl.LeftPadRight, "◉→", "Lewy touchpad →", "Lewy touchpad", "klik z prawej"),

        new(DeckControl.View, "⧉", "View ⧉", "Systemowe", "Select"),
        new(DeckControl.Menu, "≡", "Menu ≡", "Systemowe", "Start"),
        new(DeckControl.Steam, "S", "Steam", "Systemowe"),
        new(DeckControl.QuickAccess, "…", "Quick Access …", "Systemowe"),
        new(DeckControl.RightPadClick, "◎", "Prawy touchpad – klik", "Systemowe", "lizard mode: lewy klik myszy – do wyboru przytrzymaj"),
    };

    static readonly Dictionary<DeckControl, ControlInfo> ByControl = All.ToDictionary(i => i.Control);

    public static ControlInfo Of(DeckControl control) => ByControl[control];
}
