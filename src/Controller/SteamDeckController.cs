using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using HidSharp;

namespace DeckMapper;

enum ReadResult { Ok, Timeout, Disconnected }

/// <summary>
/// Odczyt wbudowanego kontrolera Steam Decka przez HID (interfejs vendor-defined).
/// Układ raportu na podstawie sterownika hid-steam z jądra Linuksa.
/// </summary>
sealed class SteamDeckController : IDisposable
{
    const int VendorId = 0x28DE;
    const int ProductId = 0x1205;
    const byte ReportDeckState = 0x09;
    const byte CmdClearDigitalMappings = 0x81;      // wyłącza klawisze lizard mode
    const byte CmdSetDefaultDigitalMappings = 0x85; // przywraca je

    public const int ReportLength = 60;

    HidDevice? _device;
    HidStream? _stream;
    byte[] _buffer = Array.Empty<byte>();

    public bool IsConnected => _stream != null;

    public bool TryConnect()
    {
        Close();
        foreach (var device in DeviceList.Local.GetHidDevices(VendorId, ProductId))
        {
            int length;
            try { length = device.GetMaxInputReportLength(); }
            catch { continue; }

            // interfejsy klawiatury/myszy lizard mode mają krótkie raporty – pomijamy
            if (length < 64) continue;
            if (!device.TryOpen(out HidStream stream)) continue;

            stream.ReadTimeout = 250;
            var buffer = new byte[length];
            if (Probe(stream, buffer))
            {
                _device = device;
                _stream = stream;
                _buffer = buffer;
                Log.Info($"Połączono z kontrolerem: {device.DevicePath}");
                return true;
            }
            stream.Dispose();
        }
        return false;
    }

    static bool Probe(HidStream stream, byte[] buffer)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 1500)
        {
            try
            {
                int n = stream.Read(buffer, 0, buffer.Length);
                if (FindHeader(buffer, n) >= 0) return true;
            }
            catch (TimeoutException) { }
            catch (Exception) { return false; }
        }
        return false;
    }

    public ReadResult Read(DeckState state)
    {
        if (_stream == null) return ReadResult.Disconnected;

        int n;
        try
        {
            n = _stream.Read(_buffer, 0, _buffer.Length);
        }
        catch (TimeoutException)
        {
            return ReadResult.Timeout;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            Log.Info("Kontroler rozłączony: " + e.Message);
            Close();
            return ReadResult.Disconnected;
        }

        int offset = FindHeader(_buffer, n);
        if (offset < 0) return ReadResult.Timeout; // inny typ raportu – ignorujemy

        Parse(_buffer.AsSpan(offset, ReportLength), state);
        return ReadResult.Ok;
    }

    /// <summary>
    /// Na Windowsie bufor może zaczynać się od bajtu report ID (0), więc szukamy nagłówka
    /// 0x01 ?? 0x09 na pozycji 0 albo 1.
    /// </summary>
    static int FindHeader(byte[] buffer, int count)
    {
        for (int o = 0; o <= 1; o++)
            if (count >= o + ReportLength && buffer[o] == 0x01 && buffer[o + 2] == ReportDeckState)
                return o;
        return -1;
    }

    static void Parse(ReadOnlySpan<byte> d, DeckState s)
    {
        var b = s.Buttons;
        Array.Clear(b);

        byte b8 = d[8], b9 = d[9], b10 = d[10], b11 = d[11], b13 = d[13], b14 = d[14];

        Set(b, DeckControl.RB, b8, 2);
        Set(b, DeckControl.LB, b8, 3);
        Set(b, DeckControl.Y, b8, 4);
        Set(b, DeckControl.B, b8, 5);
        Set(b, DeckControl.X, b8, 6);
        Set(b, DeckControl.A, b8, 7);

        Set(b, DeckControl.DpadUp, b9, 0);
        Set(b, DeckControl.DpadRight, b9, 1);
        Set(b, DeckControl.DpadLeft, b9, 2);
        Set(b, DeckControl.DpadDown, b9, 3);
        Set(b, DeckControl.View, b9, 4);
        Set(b, DeckControl.Steam, b9, 5);
        Set(b, DeckControl.Menu, b9, 6);
        Set(b, DeckControl.L5, b9, 7);

        Set(b, DeckControl.R5, b10, 0);
        Set(b, DeckControl.LeftPadClick, b10, 1);
        Set(b, DeckControl.RightPadClick, b10, 2);
        Set(b, DeckControl.L3, b10, 6);

        Set(b, DeckControl.R3, b11, 2);
        Set(b, DeckControl.L4, b13, 1);
        Set(b, DeckControl.R4, b13, 2);
        Set(b, DeckControl.QuickAccess, b14, 2);

        s.LeftPadTouched = Bit(b10, 3);
        s.RightPadTouched = Bit(b10, 4);

        s.LeftPadX = Axis(d, 16);
        s.LeftPadY = Axis(d, 18);
        s.RightPadX = Axis(d, 20);
        s.RightPadY = Axis(d, 22);

        s.LeftTrigger = Trigger(d, 44);
        s.RightTrigger = Trigger(d, 46);

        s.LeftX = Axis(d, 48);
        s.LeftY = Axis(d, 50);
        s.RightX = Axis(d, 52);
        s.RightY = Axis(d, 54);

        d.CopyTo(s.Raw);
    }

    static bool Bit(byte value, int bit) => (value & (1 << bit)) != 0;

    static void Set(bool[] buttons, DeckControl control, byte value, int bit) =>
        buttons[(int)control] = Bit(value, bit);

    static float Axis(ReadOnlySpan<byte> d, int offset) =>
        Math.Clamp(BinaryPrimitives.ReadInt16LittleEndian(d.Slice(offset, 2)) / 32767f, -1f, 1f);

    static float Trigger(ReadOnlySpan<byte> d, int offset) =>
        Math.Clamp(BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(offset, 2)) / 32767f, 0f, 1f);

    public void ClearLizardButtons() => SendCommand(CmdClearDigitalMappings);

    public void RestoreLizardButtons() => SendCommand(CmdSetDefaultDigitalMappings);

    void SendCommand(byte command)
    {
        if (_stream == null || _device == null) return;
        try
        {
            int length = _device.GetMaxFeatureReportLength();
            if (length < 3) return;
            var buffer = new byte[length]; // [0] = report ID 0
            buffer[1] = command;
            _stream.SetFeature(buffer);
        }
        catch (Exception e)
        {
            Log.Info($"Nie udało się wysłać komendy 0x{command:X2}: {e.Message}");
        }
    }

    public void Close()
    {
        _stream?.Dispose();
        _stream = null;
        _device = null;
    }

    public void Dispose() => Close();
}
