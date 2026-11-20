using System;

namespace DeckMapper;

/// <summary>Stan kontrolera z jednego raportu HID. Osie gałek: -1..1, Y w górę dodatnie.</summary>
sealed class DeckState
{
    public readonly bool[] Buttons = new bool[DeckControls.Count];
    public readonly byte[] Raw = new byte[SteamDeckController.ReportLength];

    public float LeftX, LeftY, RightX, RightY;
    public float LeftTrigger, RightTrigger;
    public float LeftPadX, LeftPadY, RightPadX, RightPadY;
    public bool LeftPadTouched, RightPadTouched;

    public void CopyTo(DeckState t)
    {
        Array.Copy(Buttons, t.Buttons, Buttons.Length);
        Array.Copy(Raw, t.Raw, Raw.Length);
        t.LeftX = LeftX; t.LeftY = LeftY; t.RightX = RightX; t.RightY = RightY;
        t.LeftTrigger = LeftTrigger; t.RightTrigger = RightTrigger;
        t.LeftPadX = LeftPadX; t.LeftPadY = LeftPadY; t.RightPadX = RightPadX; t.RightPadY = RightPadY;
        t.LeftPadTouched = LeftPadTouched; t.RightPadTouched = RightPadTouched;
    }
}
