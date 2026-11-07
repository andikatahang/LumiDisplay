using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LumiDisplay;

static class Program
{
    /// <summary>Signalled by a second launch to ask the running instance to show its window.</summary>
    public const string ShowWindowEventName = "LumiDisplay_ShowWindow";

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    private const int ASFW_ANY = -1;

    [STAThread]
    static void Main(string[] args)
    {
        // Developer utility: regenerate lumidisplay.ico from the Logo renderer
        if (args.Length == 2 && args[0] == "--export-icon")
        {
            IconExporter.Export(args[1]);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        UiFonts.Load();
        Theme.SelfCheck();

        bool autostart = Array.Exists(args, a => a == StartupManager.AutostartArg);

        // Ensure single instance; a second launch (e.g. the desktop icon) brings up the running one
        using var mutex = new Mutex(true, "LumiDisplay_SingleInstance", out bool createdNew);
        using var showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        if (!createdNew)
        {
            if (!autostart)
            {
                // Let the running instance take the foreground when it opens its window
                AllowSetForegroundWindow(ASFW_ANY);
                showWindowEvent.Set();
            }
            return;
        }

        StartupManager.RefreshStartupEntry();

        // Open the window on a normal launch; a sign-in launch stays in the tray if configured
        var settings = AppSettings.Load();
        bool openSettings = !autostart || !settings.StartMinimized
                            || Array.Exists(args, a => a == "--settings");

        Application.Run(new TrayApplicationContext(openSettings, showWindowEvent));
    }
}
