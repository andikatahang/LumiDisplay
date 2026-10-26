using System;
using System.Windows.Forms;

namespace LumiDisplay;

static class Program
{
    [STAThread]
    static void Main()
    {
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

        Application.Run(new TrayApplicationContext());
    }
}
