using System;
using System.Threading;
using System.Windows.Forms;

namespace DeckMapper;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "DeckMapper.SingleInstance", out bool created);
        if (!created)
        {
            MessageBox.Show("DeckMapper już działa – szukaj ikonki w zasobniku.", "DeckMapper");
            return;
        }

        Application.ThreadException += (_, e) => Log.Error("UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Error("Nieobsłużony wyjątek", ex);
        };

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApp());
    }
}
