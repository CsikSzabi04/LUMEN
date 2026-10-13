# Visszaállítja az asztali ikonok feliratának betűtípusát az eredeti Segoe UI-ra (az ikonok helyét nem érinti).
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class IT3 {
  [StructLayout(LayoutKind.Sequential, CharSet=System.Runtime.InteropServices.CharSet.Unicode)] public struct LOGFONT { public int Height, Width, Escapement, Orientation, Weight; public byte Italic, Underline, StrikeOut, Set, OutPrecision, ClipPrecision, Quality, PitchAndFamily; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string FaceName; }
  [DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode, SetLastError=true)] static extern bool SystemParametersInfo(uint a, uint b, ref LOGFONT lf, uint f);
  public static void Segoe() { LOGFONT lf = new LOGFONT(); SystemParametersInfo(0x1F, (uint)Marshal.SizeOf(lf), ref lf, 0); lf.FaceName = "Segoe UI"; SystemParametersInfo(0x22, (uint)Marshal.SizeOf(lf), ref lf, 3); }
}
'@
[IT3]::Segoe()
