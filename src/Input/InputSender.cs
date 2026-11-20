using System;
using System.Runtime.InteropServices;

namespace DeckMapper;

/// <summary>Wysyłanie klawiatury/myszy przez SendInput. Klawisze idą jako scancode'y.</summary>
static class InputSender
{
    const uint InputMouse = 0;
    const uint InputKeyboard = 1;

    const uint KeyEventExtended = 0x0001;
    const uint KeyEventKeyUp = 0x0002;
    const uint KeyEventUnicode = 0x0004;
    const uint KeyEventScanCode = 0x0008;

    const uint MouseMove = 0x0001;
    const uint MouseLeftDown = 0x0002, MouseLeftUp = 0x0004;
    const uint MouseRightDown = 0x0008, MouseRightUp = 0x0010;
    const uint MouseMiddleDown = 0x0020, MouseMiddleUp = 0x0040;
    const uint MouseXDown = 0x0080, MouseXUp = 0x0100;
    const uint MouseWheel = 0x0800, MouseHWheel = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    static readonly int InputSize = Marshal.SizeOf<INPUT>();

    static void Send(params INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, InputSize);

    static INPUT Keyboard(ushort scan, uint flags) => new()
    {
        type = InputKeyboard,
        u = new InputUnion { ki = new KEYBDINPUT { wScan = scan, dwFlags = flags } },
    };

    static INPUT Mouse(uint flags, uint data = 0, int dx = 0, int dy = 0) => new()
    {
        type = InputMouse,
        u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } },
    };

    static uint KeyFlags(KeyDef key, bool up) =>
        KeyEventScanCode | (key.Extended ? KeyEventExtended : 0) | (up ? KeyEventKeyUp : 0);

    public static void KeyDown(KeyDef key) => Send(Keyboard(key.ScanCode, KeyFlags(key, false)));

    public static void KeyUp(KeyDef key) => Send(Keyboard(key.ScanCode, KeyFlags(key, true)));

    public static void TapKey(KeyDef key) =>
        Send(Keyboard(key.ScanCode, KeyFlags(key, false)), Keyboard(key.ScanCode, KeyFlags(key, true)));

    public static void TypeChar(char c) =>
        Send(Keyboard(c, KeyEventUnicode), Keyboard(c, KeyEventUnicode | KeyEventKeyUp));

    public static void TypeText(string text)
    {
        foreach (char c in text)
        {
            switch (c)
            {
                case '\r': break;
                case '\n': TapKey(new KeyDef(0x1C, false)); break; // Enter
                case '\t': TapKey(new KeyDef(0x0F, false)); break; // Tab
                default: TypeChar(c); break;
            }
        }
    }

    public static void MouseButtonEvent(MouseButton button, bool down)
    {
        var (flags, data) = button switch
        {
            MouseButton.Left => (down ? MouseLeftDown : MouseLeftUp, 0u),
            MouseButton.Right => (down ? MouseRightDown : MouseRightUp, 0u),
            MouseButton.Middle => (down ? MouseMiddleDown : MouseMiddleUp, 0u),
            MouseButton.X1 => (down ? MouseXDown : MouseXUp, 1u),
            MouseButton.X2 => (down ? MouseXDown : MouseXUp, 2u),
            _ => (0u, 0u),
        };
        if (flags != 0) Send(Mouse(flags, data));
    }

    public static void Wheel(int delta, bool horizontal) =>
        Send(Mouse(horizontal ? MouseHWheel : MouseWheel, unchecked((uint)delta)));

    public static void MoveMouse(int dx, int dy) => Send(Mouse(MouseMove, 0, dx, dy));
}
