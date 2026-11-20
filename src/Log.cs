using System;
using System.IO;

namespace DeckMapper;

/// <summary>Prosty log do pliku deckmapper.log obok exe.</summary>
static class Log
{
    static readonly object Gate = new();
    static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "deckmapper.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string context, Exception ex) => Write("ERROR", $"{context}: {ex}");

    static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
        }
        catch
        {
            // log nie może wywrócić aplikacji
        }
    }
}
