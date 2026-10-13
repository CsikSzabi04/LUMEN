// A FEATURES.md előállítása a műveleti jegyzékből + a nem műveletként elérhető beépített képességek listájából.
using System;
using System.Collections.Generic;
using System.Text;

namespace GlassTerm
{
    public static class FeatureDoc
    {
        public static readonly string[] Flagships = {
            Loc.T("Lapok és osztott panelek – több shell egy ablakban, húzható elválasztók, panel-kinagyítás, lap-átnevezés/színezés/átrendezés, bezárt lap újranyitása"),
            Loc.T("Parancspaletta (Ctrl+Shift+P) – minden funkció egy helyen, fuzzy kereséssel; a gyorsbillentyűk a keys.ini-ben átírhatók"),
            Loc.T("Fuzzy előzmény-kereső (Ctrl+R) – a PowerShell teljes előzményében és az összes nyitott lap parancsaiban keres, Shift+Enter azonnal futtat"),
            Loc.T("Parancs-blokkok – minden parancs mellett ✓/✗ jelvény + futási idő, hibánál piros sáv; Ctrl+Fel/Le ugrás a parancsok között, az utolsó kimenet egy kattintással másolható/kijelölhető, a parancs újrafuttatható"),
            Loc.T("Kattintható linkek és fájlútvonalak – URL a böngészőben, fájl:sor:oszlop a szerkesztőben (VS Code / Antigravity), mappa az Intézőben, relatív útvonalak a munkamappához képest"),
            Loc.T("Keresés a teljes görgetősávban – találat-számláló, kis/nagybetű és regex kapcsoló, F3 / Shift+F3"),
            Loc.T("Beillesztés-védelem – többsoros szöveg beillesztése előtt jóváhagyás, okos idézőjel/kötőjel javítás, egy sorba / idézőjelezett útvonalként beillesztés"),
            Loc.T("Quake mód – globális gyorsbillentyűvel (Ctrl+Alt+Space) a képernyő tetejéről lecsúszó terminál"),
            Loc.T("Profilok és munkamenet-visszaállítás – PowerShell, pwsh, cmd, Git Bash, WSL, Python, Node, SSH-gazdák automatikusan; induláskor a lapok és mappák visszatérnek"),
            Loc.T("Élő előnézet a terminál alatt – localhost-os cím (Vite, Next, Express…) felismerése, „Előnézet alul” egy kattintással vagy `preview 5173` paranccsal; valódi Edge-motor, DevTools, telefon/tablet nézet"),
            Loc.T("Értesítés hosszú parancsra – ha a parancs elfut, miközben máshol dolgozol: villogó tálcagomb + buborék, lap-jelzés, a végén idő és kilépési kód")
        };

        public static readonly string[] Builtin = {
            Loc.T("14 beépített színtéma, élő váltással"), Loc.T("DejaVu Sans Mono beágyazva, ligatúra-mentes, pontos cellakirajzolás"), Loc.T("Truecolor (24 bit) és 256 színű paletta"),
            Loc.T("Félkövér, halvány, dőlt, aláhúzott, áthúzott, fordított, villogó karakterek"), Loc.T("Dupla aláhúzás és színes aláhúzás"), Loc.T("Teljes Unicode, széles (CJK, emoji) karakterek két cellában"),
            Loc.T("Alternatív képernyő (vim, less, htop, fzf)"), Loc.T("Görgetősáv húzható csúszkával"), Loc.T("Kurzor-forma és villogás, alkalmazás által állítható (DECSCUSR)"),
            Loc.T("Egérjelentés (1000/1002/1003, SGR 1006) – mc, htop, vim egér támogatás"), Loc.T("Fókusz-események (1004)"), Loc.T("Bracketed paste mód"), Loc.T("OSC 0/2 ablakcím"), Loc.T("OSC 7 munkamappa-követés"), Loc.T("OSC 9 / 777 értesítések"),
            Loc.T("OSC 52 vágólap-írás (távoli gépről is)"), Loc.T("OSC 133 kilépési kód"), Loc.T("Kijelölés: dupla kattintás szó, hármas kattintás sor, Alt+húzás téglalap"), Loc.T("Automatikus másolás kijelöléskor (kapcsolható)"),
            Loc.T("Középső gomb = beillesztés (kapcsolható)"), Loc.T("Ctrl+görgetés = nagyítás"), Loc.T("Sima görgetés, finomhangolható sebesség"), Loc.T("Fájl / mappa ráhúzása a terminálra: útvonal beillesztése idézőjelezve"), Loc.T("Vizuális és hangos csengő"),
            Loc.T("Inaktív panelek halványítása"), Loc.T("Munkamappa követése lapnévként"), Loc.T("Futó parancs neve a lapon"), Loc.T("Lap-aktivitás és csengő jelzése háttérlapon"), Loc.T("Broadcast – a gépelés minden panelre megy"),
            Loc.T("Munkamenet-napló fájlba (nyers kimenet)"), Loc.T("Export: szöveg, HTML (színekkel), PNG, vágólapra HTML/kép"), Loc.T("Glass kontextusmenü mindenhol"), Loc.T("Snippetek változókkal ({clip} {cwd} {date} {ask:…})"), Loc.T("Vágólap-előzmény (40 elem)"),
            Loc.T("Gyorsbillentyű-felülírás a keys.ini-ben"), Loc.T("Beállítások a term.ini-ben, élő újratöltés"), Loc.T("Saját PowerShell-kiegészítések: lumen-user.ps1"), Loc.T("Beépített PowerShell-segédparancsok (lumen help)")
        };

        public static string Build(TermHost h)
        {
            StringBuilder sb = new StringBuilder(); int n = 0;
            sb.AppendLine(Loc.T("# Lumen Terminal – funkciók")); sb.AppendLine();
            sb.AppendLine(Loc.T("A lista a parancspaletta (Ctrl+Shift+P) valódi műveleti jegyzékéből készül, így mindig naprakész. Gyorsbillentyűk: `keys.ini` (`azonosító=Ctrl+Shift+X`)."));
            sb.AppendLine(); sb.AppendLine(Loc.T("## Nagy újítások (amit a sima cmd nem tud)")); sb.AppendLine();
            for (int i = 0; i < Flagships.Length; i++) sb.AppendLine((i + 1) + ". " + Flagships[i]);
            n += Flagships.Length;
            sb.AppendLine(); sb.AppendLine(Loc.T("## Műveletek a parancspalettában")); string cat = null; int actions = 0;
            foreach (TermAction a in h.AllActions)
            {
                if (a.Cat != cat) { cat = a.Cat; sb.AppendLine(); sb.AppendLine("### " + cat); sb.AppendLine(); }
                sb.AppendLine("- " + a.Title + (string.IsNullOrEmpty(a.Key) ? "" : "  — `" + a.Key + "`") + "  *(" + a.Id + ")*"); actions++;
            }
            sb.AppendLine(); sb.AppendLine(Loc.T("## Beépített működés (nem műveletként elérhető)")); sb.AppendLine();
            foreach (string b in Builtin) sb.AppendLine("- " + b);
            n += actions + Builtin.Length;
            sb.AppendLine(); sb.AppendLine("---"); sb.AppendLine(Loc.T("Összesen: ") + n + Loc.T(" funkció (") + Flagships.Length + Loc.T(" nagy újítás + ") + actions + Loc.T(" paletta-művelet + ") + Builtin.Length + Loc.T(" beépített képesség)."));
            return sb.ToString();
        }
    }
}
