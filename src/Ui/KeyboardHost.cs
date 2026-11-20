namespace DeckMapper;

/// <summary>Jedna wspólna klawiatura ekranowa dla zasobnika, akcji @Keyboard i pól tekstowych w edytorze.</summary>
sealed class KeyboardHost : System.IDisposable
{
    OnScreenKeyboard? _keyboard;

    OnScreenKeyboard Keyboard
    {
        get
        {
            if (_keyboard == null || _keyboard.IsDisposed) _keyboard = new OnScreenKeyboard();
            return _keyboard;
        }
    }

    public bool Visible => _keyboard is { IsDisposed: false, Visible: true };

    public void Toggle()
    {
        var keyboard = Keyboard;
        if (keyboard.Visible) keyboard.Hide();
        else keyboard.ShowKeyboard();
    }

    public void ShowForTyping() => Keyboard.ShowForTyping();

    public void Hide()
    {
        if (Visible) _keyboard!.Hide();
    }

    public void Dispose() => _keyboard?.Dispose();
}
