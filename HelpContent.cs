// A Súgó tartalma: kategória (##), csoport (#) és elem (icon|cím|állapot|leírás|művelet) sorok.
using System;
using System.Collections.Generic;

namespace GlassControlWpf
{
    public static class HelpContent
    {
        const string Data = @"
## term|🖥|Terminál
# 🗂|Lapok, panelek, ablak
🗂|Lapok és osztott panelek|Kész|Ctrl+Shift+T új lap, Ctrl+Shift+D / E jobbra / lefelé osztás, húzható elválasztó, Ctrl+Shift+Z panel kinagyítása. A lap átnevezhető, színezhető, húzással átrendezhető, a bezárt lap visszahozható (Ctrl+Shift+U).|tab:5
⌘|Parancspaletta|Kész|Ctrl+Shift+P: 200+ művelet fuzzy kereséssel (témák, betűk, export, profilok, kapcsolók). A gyorsbillentyűk a terminal\keys.ini fájlban átírhatók (azonosító=Ctrl+Shift+X).|tab:5
🔽|Quake mód|Kész|Terminal.exe /quake: a képernyő tetejéről lecsúszó terminál, globális Ctrl+Alt+Space gyorsbillentyűvel (quakekey= a term.ini-ben).
👤|Profilok és munkamenet|Kész|PowerShell, pwsh, cmd, Git Bash, WSL, Python, Node, SSH-gazdák automatikusan; induláskor a lapok és mappák visszatérnek. Saját profil: profile.N=név;parancs;mappa.
# 🧱|Parancsok és kimenet
🧱|Parancs-blokkok|Kész|Minden parancs mellett ✓/✗ jelvény és futási idő, hibánál piros sáv. Ctrl+Fel/Le ugrás a parancsok között, Ctrl+Shift+O az utolsó kimenet másolása, újrafuttatás.
🔎|Előzmény-kereső|Kész|Ctrl+R: a PowerShell előzményei és a nyitott lapok parancsai között keres; Enter beírja, Shift+Enter lefuttatja.
🔗|Linkek és útvonalak|Kész|A http(s) link sima kattintásra megnyílik a böngészőben; fájl:sor:oszlop útvonal Ctrl+kattintásra a szerkesztőben. Futtatható fájlt kattintásra sosem indít.
🔍|Keresés|Kész|Ctrl+F: a teljes görgetősávban, regex és kis/nagybetű kapcsolóval, F3 léptetés.
📋|Beillesztés-védelem|Kész|Többsoros szövegnél jóváhagyás, veszélyes parancsnál külön figyelmeztetés (csak Y fogadja el), okos idézőjel-javítás, vezérlőkarakterek kiszűrése.
🔔|Hosszú parancs értesítés|Kész|10 mp felett tálcavillogás, buborék és lapjelzés, ha máshol dolgozol.
# 📺|Előnézet és eszközök
📺|Előnézet a terminál alatt|Kész|localhost-cím (Vite, Next…) felismerése: „Előnézet alul” sáv; kézzel `preview 5173`, Ctrl+Shift+J. Edge-motor, DevTools, telefon/tablet szélesség.|tab:5
✂|Snippetek|Kész|Ctrl+Shift+S: snippets.txt, változók: {ask:…} {clip} {cwd} {date}.
🧾|Export és napló|Kész|Szöveg, HTML (színekkel), PNG; munkamenet-napló fájlba; vágólap-előzmény (Ctrl+Shift+H).
🕶|Titok-őr|Kész|A kimenetben felismert API-kulcsokat/tokeneket (sk-…, ghp_…, AKIA…, JWT) elmossa; egérrel fölé víve látszanak. Palettából kapcsolható.

## hub|🧰|Fejlesztői Hub
🧰|Eszközök|Kész|29 eszköz valódi verzióval, egy kattintásos telepítés / frissítés / eltávolítás (winget), csoportos telepítés pipákkal.|hub:0
🔎|Winget-kereső|Kész|Keresés a teljes winget-katalógusban. Ha a winget forrás (cdn.winget.microsoft.com) nem elérhető, a Hub jelzi, és a gyártó oldalát ajánlja.|hub:1
📁|Projektek|Kész|Automatikus keresés, típus- és git-állapot, szkriptek gombként, git műveletek, build-mappák méretezése és törlése.|hub:2
✨|Új projekt|Kész|13 sablon (React/Vite, Next, Vue, Svelte, weboldal, Express, Python, FastAPI, .NET, Rust, Go).|hub:3
🔌|Portok|Kész|Élő lista, megnyitás, URL másolás, leállítás, és „📺 Terminal preview” gomb minden sorban.|hub:4
🌐|Környezet|Kész|PATH-szerkesztő (REG_EXPAND_SZ megőrzésével), változók, git beállítások, SSH-kulcs.|hub:5
⚡|Gyorseszközök|Kész|49 szövegeszköz: JSON, Base64, JWT, hash, UUID, jelszó, epoch, regex-teszt…|hub:6
🧹|Takarítás|Kész|Csomag-gyorsítótárak, Temp, projektek node_modules és build-mappái, docker prune.|hub:7
🐳|Docker|Kész|Konténerek és képek kezelése, napló, shell.|hub:8

## lumen|💾|Lumen
💾|Memória-seprés|Kész|Az „Optimalizálás” gomb minden folyamat munkakészletét összehúzza (védett rendszerfolyamatok kimaradnak). Automatikus mód küszöbbel, cooldown-nal, tétlenség-figyeléssel; teljes képernyős játék/videó közben nem fut.|tab:0
📊|Feladatkezelő|Kész|Folyamatlista keresővel, RAM ürítés, leállítás megerősítéssel, jobb klikk menü, Boncolás.|tab:1
📈|Analitika|Kész|5 mp-enkénti mérés, program-szintű átlag/csúcs/trend, tanuló rendszer (analytics mappa).|tab:2
🍏|iOS Glass UI|Kész|A dokk, pillek, Start menü és tálca stílusa élő csúszkákkal.|tab:3
🔬|Folyamat-boncolás|Kész|A feladatkezelőben jobb klikk → Boncolás: szülő-lánc, parancssor, nyitott portok, indítási bejegyzések, és egy kattintásos „indításkor tiltás” (HKCU).|tab:1
🎯|Fókusz-mód|Kész|A wiper oldalon: egy gomb seper, szünetelteti a Wallpaper Enginet és elnémítja a felugró értesítéseket; újra megnyomva minden visszaáll.|tab:0
🔮|Memória-előrejelzés|Kész|A wiper oldalon élő sor: ha a memóriahasználat emelkedik, megbecsli, mikor éri el a 95%-ot.|tab:0
🛡|Megbízhatóság|Kész|Atomi fájlírás, naplók mérete korlátos, védett folyamatok, PID-ellenőrzés leállításkor, kis képernyőre igazított ablak.

## dock|🪟|Dokk és Spotlight
🪟|Dokk és pillek|Kész|Üveg dokk, élő téma a settings.ini-ből, Start- és óra-pillek.|tab:3
🚀|Start menü|Kész|Saját Start menü (Win gomb), átrendezhető, oldalakkal.
🔦|Spotlight|Kész|Alt+Space vagy Win+S: alkalmazások, fájlok, számolás, webes keresés.
⚡|Spotlight-akciók|Kész|Írd be: `port 3000` (felszabadítás), `ram` (azonnali seprés) – a Spotlight műveletet is végez, nem csak keres.
📎|Vágólap-érzékeny Spotlight|Kész|Üres lekérdezésnél a vágólap tartalmához kínál műveletet: link megnyitása, JSON szépítése, útvonal megmutatása.
🧷|Megbízható horog|Kész|A Win-gomb horga saját szálon fut kivételvédelemmel, így a Windows nem szedi le.

## idea1|💡|Ötletek – terminál
💡|Időgép-blokkok|Terv|Minden parancs kimenete pillanatképként megmarad; két futás kimenete színes diff-fel összevethető.
💡|Szemétkosár a veszélyes parancsoknak|Terv|Az rm -rf / git clean helyett árnyék-kukába kerülnek a fájlok, 24 óráig visszahozhatók.
💡|Hiba-magyarázó a blokkon|Terv|Piros blokk → egy kattintással a hiba és a projekt környezete egy AI-hoz megy; a javaslat futtatható paranccsal tér vissza.
💡|Visszajátszható run book|Kész|Paletta: „Run book” – az utolsó 10 blokk (parancs, állapot, idő, kimenet) önálló HTML-be az asztalra. (Lépésenkénti lejátszó még nincs.)|tab:5
💡|Figyelő-blokk bármire|Kész|Paletta: „Figyelő-blokk” – az utolsó parancs újrafut, ha a mappában fájl változik (node_modules, .git, bin, obj, dist kimarad). Kapcsolóként működik.|tab:5
💡|Dev-szerver szellem-panel|Terv|A hosszan futó szerver kimenete egysoros élő állapotjelzővé zsugorodik a lapsávban.
💡|Kimenet-térkép|Terv|Minimap a görgetősáv mellett: blokkok színezve, hibák piros jellel, kimenet-hajtogatás.
💡|Személyes parancs-előrejelzés|Terv|Szürke szellem-javaslat a saját előzményeidből (mappa, git-ág, napszak szerint), helyi modellel.
💡|OCR-beillesztés|Terv|Win+Shift+T: képernyőrészletből (Windows OCR) szöveg a terminálba.
💡|Előnézet mobil-teszttel|Terv|Hálózat-szimuláció, sötét/világos váltás, QR-kód a telefonos teszthez.
💡|Hangvezérelt paletta|Terv|„Indítsd a dev szervert” offline beszédfelismeréssel.

## idea2|💡|Ötletek – Lumen, dokk
💡|Projekt-memóriamérleg|Terv|A folyamatokat projektekhez rendeli („climarkai-website: 2,1 GB”), egy gombbal felfüggeszthető/folytatható.
💡|Böngésző-fül fagyasztó|Terv|Brave/Chrome füleket külön rangsorol memória szerint, és bezárás nélkül lefagyasztja őket.
💡|Alvó localhost|Terv|A tétlen dev szervert felfüggeszti, az első kérésnél egy kis proxy visszaébreszti.
💡|Lusta autostart|Terv|Az induló programok egyenként, tétlenség után indulnak: a bejelentkezés azonnal használható.
💡|Háttérkép-követő téma|Terv|A Wallpaper Engine élő képéből palettát nyer, és az üvegfelületek színe lassan vele változik.
💡|Projekt-munkaterület recept|Terv|Szerkesztő, terminál-lapok, böngésző, Docker és ablakelrendezés egy kattintással a dokkról.
💡|Munkamenet időgép|Terv|Ablakok, terminál-lapok, git stash, böngészőfülek lefagyasztása; hétfőn visszaáll a péntek 17:00-s állapot.
💡|Élő dokk-jelvények|Terv|VS Code ikonon bukó tesztek száma, Terminal ikonon futó blokkok előrehaladás-gyűrűje.
💡|Heti „mi húzza le a gépet?” riport|Kész|A wiper oldalon jobb klikk → „Heti riport”: HTML kártya a napi átlagokkal, csúcsokkal, legnagyobb fogyasztókkal, seprésekkel.|tab:0

## mine|📝|Saját ötleteim
";
        public static void Build(List<HelpItem> roots, Dictionary<string, HelpItem> byId)
        {
            HelpItem cat = null, grp = null; int n = 0;
            foreach (string raw in Data.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim(); if (line.Length == 0) continue;
                if (line.StartsWith("## ")) { string[] p = line.Substring(3).Split('|'); cat = new HelpItem { Id = p[0], Icon = p[1], Title = Loc.T(p[2]), Depth = 0 }; roots.Add(cat); byId[cat.Id] = cat; grp = null; }
                else if (line.StartsWith("# ")) { string[] p = line.Substring(2).Split('|'); grp = new HelpItem { Id = "g" + (n++), Icon = p[0], Title = Loc.T(p[1]), Depth = 1, Parent = cat }; cat.Kids.Add(grp); byId[grp.Id] = grp; }
                else
                {
                    string[] p = line.Split('|'); HelpItem parent = grp ?? cat; HelpItem it = new HelpItem { Id = "i" + (n++), Icon = p[0], Title = Loc.T(p[1]), Status = Loc.T(p[2]), Desc = Loc.T(p[3]), Action = p.Length > 4 ? p[4] : null, Parent = parent, Depth = parent.Depth + 1 };
                    parent.Kids.Add(it); byId[it.Id] = it;
                }
            }
            foreach (HelpItem r in roots) if (r.Id == "idea1" || r.Id == "idea2") r.Collapsed = false;
        }
    }
}
