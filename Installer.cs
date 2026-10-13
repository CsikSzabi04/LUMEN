// Lumen – első indításkor "települ": parancsikon az asztalra és a Start-menübe (a Spotlight/Start keresés is megtalálja), a régi
// "GlassControl Suite" parancsikonok takarítása. Egyszeri (install.ini jelzi); a fejléc jobb klikk menüjében újra létrehozható.
using System;
using System.IO;
using System.Reflection;

namespace GlassControlWpf
{
    public static class Installer
    {
        public const string AppName = "Lumen", Tagline = "üveg vezérlőközpont";
        public static bool JustInstalled;

        static string Exe { get { return Assembly.GetExecutingAssembly().Location; } }
        static string Dir { get { return Path.GetDirectoryName(Exe); } }
        static string Marker { get { return Path.Combine(Dir, "install.ini"); } }
        public static string DesktopLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"); } }
        public static string StartLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs\" + AppName + ".lnk"); } }

        static string AltMarker { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Lumen\install.ini"); } }
        static bool MarkerOk(string f) { try { if (!File.Exists(f)) return false; foreach (string l in File.ReadAllLines(f)) if (l.StartsWith("exe=", StringComparison.Ordinal) && string.Equals(l.Substring(4).Trim(), Exe, StringComparison.OrdinalIgnoreCase)) return true; } catch { } return false; }
        // csak akkor "telepített", ha a jelölő ugyanerre az exe-útvonalra mutat (mappa-áthelyezés után újra létrejönnek a parancsikonok)
        public static bool IsInstalled { get { return MarkerOk(Marker) || MarkerOk(AltMarker); } }

        // csak akkor csinál bármit, ha még nincs telepítve (vagy force)
        public static void EnsureInstalled(bool force)
        {
            if (!force && IsInstalled) return;
            try
            {
                Shortcut(DesktopLnk); Shortcut(StartLnk); CleanOld();
                string mk = "installed=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + Environment.NewLine + "exe=" + Exe + Environment.NewLine;
                try { File.WriteAllText(Marker, mk); } catch { try { Directory.CreateDirectory(Path.GetDirectoryName(AltMarker)); File.WriteAllText(AltMarker, mk); } catch { } }      // csak olvasható mappa esetén a profilba írjuk a jelölőt
                JustInstalled = true;
            }
            catch { }
        }

        public static void Uninstall()
        {
            try { if (File.Exists(DesktopLnk)) File.Delete(DesktopLnk); } catch { }
            try { if (File.Exists(StartLnk)) File.Delete(StartLnk); } catch { }
            try { if (File.Exists(Marker)) File.Delete(Marker); } catch { }
            try { if (File.Exists(AltMarker)) File.Delete(AltMarker); } catch { }
            try { using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) { if (k != null) k.DeleteValue("GlassControl", false); } } catch { }       // az automatikus indítás is megszűnik
        }

        static void Shortcut(string lnk)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell"); object sh = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk }); Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { Exe });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Dir });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { AppName + " – " + Tagline });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { Exe + ",0" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sc); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sh); } catch { }
        }

        // a korábbi automatikus "GlassControl Suite" parancsikonok (Start-menü, tálca-mappa) – csak ha a mi exe-nkre mutatnak
        static void CleanOld()
        {
            string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            foreach (string f in new string[] { Path.Combine(app, @"Microsoft\Windows\Start Menu\Programs\GlassControl Suite.lnk"), Path.Combine(app, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\GlassControl Suite.lnk") })
            {
                try
                {
                    if (!File.Exists(f)) continue; Type t = Type.GetTypeFromProgID("WScript.Shell"); object sh = Activator.CreateInstance(t); object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { f });
                    string target = (string)sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null);
                    if (string.Equals(target, Exe, StringComparison.OrdinalIgnoreCase)) File.Delete(f);
                }
                catch { }
            }
        }
    }
}
