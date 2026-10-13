using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace GlassControlLauncher
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            string currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string startBat = Path.Combine(currentDir, "start.bat");

            if (!File.Exists(startBat))
            {
                MessageBox.Show("A start.bat fajl nem talalhato ebben a mappaban:\n" + startBat, "GlassControl Hiba", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c \"" + startBat + "\"";
            psi.WorkingDirectory = currentDir;
            psi.UseShellExecute = true;

            try
            {
                // Próbáljuk meg Adminisztrátorként indítani
                psi.Verb = "runas";
                Process.Start(psi);
            }
            catch
            {
                // Ha a felhasználó elutasította az UAC-ot, elindítjuk normál módban
                try
                {
                    psi.Verb = "";
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Nem sikerult elinditani a GlassControl Suite-ot:\n" + ex.Message, "GlassControl Launch Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
    }
}
