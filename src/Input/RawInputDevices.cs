using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DeckMapper;

/// <summary>Lista klawiatur z Raw Input (GetRawInputDeviceList) – do ostrzeżenia o klawiaturze USB przy filtrze.</summary>
static class RawInputDevices
{
    const uint RimTypeKeyboard = 1;
    const uint RidiDeviceName = 0x20000007;

    [StructLayout(LayoutKind.Sequential)]
    struct RawInputDeviceList { public IntPtr hDevice; public uint dwType; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetRawInputDeviceList([Out] RawInputDeviceList[]? list, ref uint count, uint size);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, [Out] char[]? data, ref uint size);

    /// <summary>Ścieżki urządzeń wszystkich klawiatur widzianych przez Windows (także wirtualnych).</summary>
    public static List<string> KeyboardPaths()
    {
        var result = new List<string>();
        try
        {
            uint size = (uint)Marshal.SizeOf<RawInputDeviceList>();
            uint count = 0;
            if (GetRawInputDeviceList(null, ref count, size) != 0 || count == 0) return result;

            var list = new RawInputDeviceList[count];
            uint got = GetRawInputDeviceList(list, ref count, size);
            if (got == uint.MaxValue) return result;

            for (int i = 0; i < got; i++)
            {
                if (list[i].dwType != RimTypeKeyboard) continue;
                uint chars = 0;
                GetRawInputDeviceInfo(list[i].hDevice, RidiDeviceName, null, ref chars);
                if (chars == 0) continue;
                var name = new char[chars];
                if (GetRawInputDeviceInfo(list[i].hDevice, RidiDeviceName, name, ref chars) == uint.MaxValue) continue;
                result.Add(new string(name).TrimEnd('\0'));
            }
        }
        catch (Exception e)
        {
            Log.Error("Lista klawiatur (Raw Input)", e);
        }
        return result;
    }
}
