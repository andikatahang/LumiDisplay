using System;
using System.Windows.Forms;

namespace LumiDisplay;

static class Program
{
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

        // Ensure single instance
        using var mutex = new System.Threading.Mutex(true, "LumiDisplay_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("LumiDisplay is already running.", "LumiDisplay",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // --settings opens the settings window right after launch
        bool openSettings = Array.Exists(args, a => a == "--settings");
        Application.Run(new TrayApplicationContext(openSettings));
    }
}
