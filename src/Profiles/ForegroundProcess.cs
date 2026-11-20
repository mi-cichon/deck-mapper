using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace DeckMapper;

/// <summary>Nazwa procesu okna na pierwszym planie (do automatycznego przełączania profili).</summary>
static class ForegroundProcess
{
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static readonly int OwnProcessId = Environment.ProcessId;

    /// <summary>Nazwa bez ".exe" (np. "gta_sa") albo null. ownProcess = okno należy do DeckMappera.</summary>
    public static string? GetName(out bool ownProcess)
    {
        ownProcess = false;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;
            if (pid == OwnProcessId)
            {
                ownProcess = true;
                return null;
            }
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Uruchomione programy z oknem: (proces.exe, tytuł okna).</summary>
    public static List<(string Process, string Title)> ListWindowed()
    {
        var result = new List<(string, string)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id != OwnProcessId && p.MainWindowHandle != IntPtr.Zero)
                    result.Add((p.ProcessName + ".exe", p.MainWindowTitle));
            }
            catch
            {
                // proces mógł się zakończyć albo nie mamy dostępu
            }
            finally
            {
                p.Dispose();
            }
        }
        return result
            .GroupBy(r => r.Item1, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(r => r.Item1, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
