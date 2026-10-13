# ADVANCED_USE

## dock\ — GlassDock: kézzel mozgatható, átméretezhető, elmosott üveg "pilula" (Windows 10, mindkét monitoron)
Saját program (C#/WPF, `GlassDock.cs`; `Build.ps1` fordítja, nem kell hozzá telepítés).
- **Üveg:** a dokk a mögötte lévő képet lefotózza és erősen elmossa, erre jön a sötét árnyalat, ezért semmi nem látszik át élesen. Az árnyalat sötétsége: Shift+görgő vagy jobb klikk → Üveghatás / sötétség. A háttérfotó a dokk megjelenésekor (és mozgatás/átméretezés után) készül, ezért a dokk a képernyőképeken is rendesen látszik.
- **Ikonok:** minden ikon (az újak is) iOS-szerű üveg csempén van, alapból fekete-fehérben (jobb klikk: Fekete-fehér ikonok / Üveg ikonháttér; `settings.ini`: `mono=0`, `tiles=0`).
- **Asztali parancsikonok:** minden `.lnk` és `.url` az asztalon (felhasználói + közös asztal) ugyanazt az üveg csempés, fekete-fehér ikont kapja; az **újonnan létrehozottak is automatikusan** (a futó dokk figyeli az asztalt). Az ikonok helyét és a parancsikonok célját nem módosítja, csak az ikont. Az eredetik mentve: `dock\desktop-tilesackup` és `desktop-shortcuts-backup`. Visszaállítás: `dock\Restore-DesktopIcons.ps1`. Kikapcsolás: `settings.ini` → `desktoptiles=0`. Nem érintett: sima `.exe`, mappák, Lomtár (azokhoz a Windows nem enged ikoncserét).
- **Animációk:** a dokk, a két oldalsó pilula és az előnézet iOS-szerűen csúszva és áttűnve jelenik meg / tűnik el (rugós lassulás); az ikon lenyomáskor kicsit összehúzódik, felengedéskor "pattan". Animáción kívül a program ~50 ms-enként dolgozik, így alig terheli a gépet.
- **Mozgatás:** bal egérgombbal húzd a dokkot bárhol (ikonról is). **Átméretezés:** húzd a szélét; az ikonok mindig kitöltik és követik a méretet.
- **Egérgörgő:** Ctrl+görgő = nagyítás · Ctrl+Shift+görgő = szélesség · Shift+görgő = üveg sötétsége.
- **Ikon:** kattintás = indítás / előhozás / minimalizálás · Shift+húzás = sorrend · jobb klikk = kitűzés/levétel.
- **Több ablakos program** (pl. Brave): fölé víve élő előnézet-kártyák (kattintás = előhoz, × vagy középső gomb = bezár). Kattintáskor az előnézet eltűnik.
- **Automatikus elrejtés (a dokk helyéhez igazodik):** a dokk ahhoz a képernyőszélhez bukkan fel, amelyikhez a legközelebb van (alul, fent, vagy oldalt; csak külső szélek, két monitor között nem). Ha a dokk **alul** van, az alsó élre víve a dokk és a Start/óra pilulák **együtt** jönnek elő. Ha a dokk máshol van (pl. **fent**), a felső élre víve csak a dokk jön elő, a Start/óra pilulák pedig csak az alsó élre vitt egérre, külön. Mindig a dokk szélének irányából csúsznak be és ki. (Jobb klikk menüben kikapcsolható.)
- **Oldalsó pilulák:** a Windows tálca Start gombja (bal sarok) és óra/tálcaikonjai (jobb sarok) ugyanilyen üveg-pilulában vannak. Pontosan a dokkal együtt jelennek meg és tűnnek el (az alsó élre víve előjönnek, amíg az egér rajtuk vagy a dokkon van, vagy nyitva van a Start/naptár). A 2. monitoron nincs tálca. A Windows tálca automatikus elrejtésű, így nem foglal helyet: a kimaximalizált ablak a teljes képernyőt kitölti, az asztali ikonok helyét nem érinti.
- Jobb klikk a dokkon: méret, szélesség, üveghatás, lekerekítés, Start gomb a dokkban (alapból ki), automatikus elrejtés, tálca ikonok elrejtése, alkalmazás hozzáadása, alaphelyzet, kilépés.
- `settings.ini`: `glass` (sötétség), `round`, `pills=0` (oldalsó pilulák ki), `hidetb=0` (tálca ikonjai vissza), `tbautohide=0` (a Windows tálca ne legyen automatikus elrejtésű), `start=1`.
- Ha a dokk nem a "Kilépés"-sel állt le: `Restore-Taskbar.ps1` visszahozza a tálca ikonjait és hátterét. Hibakeresés: `GlassDock.exe /noexcl` (a dokk látszik képernyőképen, elmosás nélkül), `/dumpfrost`.

## Asztali ikonok elrendezése (alap)
- Az alap elhelyezés az, amit a 18:41-es képernyőképen küldtél (két monitor). Mentve név szerint: `dock\Restore-DesktopLayout.ps1`, és a rendszerleíró adatbázisból is: `desktop-layout-default.reg`, `desktop-positions.txt`.
- Ha az ikonok valaha összekeverednének: `dock\Restore-DesktopLayout.ps1 -Apply` visszateszi mindet a helyére (csak az ikonok helyét állítja, többször ismétli, amíg mindegyik a helyén van). Paraméter nélkül csak ellenőriz.
- Ha szándékosan átrendezed az ikonokat, és az legyen az új alap, szólj, vagy frissítsd a fájl elején a koordinátákat.

## Saját Start-menü (GlassStart)
- A Win billentyű és a bal alsó Start-pilula a dokk saját, üveges Start-menüjét nyitja (a Windowsé helyett): keresőmező, 5 oszlopos alkalmazásrács üveg csempékkel és fekete-fehér ikonokkal (az összes telepített alkalmazás, az újak is automatikusan), alul Beállítások és Kikapcsolás (Alvás / Újraindítás / Leállítás / Kijelentkezés).
- Gépelj keresni, Enter = az első találat, Esc vagy mellékattintás = bezár. Win+E, Win+D stb. kombinációk a megszokott módon működnek.
- Kikapcsolás (a Windows saját Start-menüje vissza): `dock\settings.ini` → `glassstart=0`, dokk újraindítása.
- A Windows élő csempéi nincsenek benne.

## Terminal (macOS Terminal kinézet) – `terminal\Terminal.exe`
- Valódi terminál (ConPTY + saját VT-emulátor): PowerShell fut benne `felhasznalo@gep ~ % ` prompttal és "Last login" sorral, színekkel, görgetéssel, kijelöléssel. Betű: DejaVu Sans Mono (a Menlo alapja), a `terminal\fonts` mappából.
- Kinézet: piros/sárga/zöld gombok, szürke címsor (`felhasznalo — powershell — 100×27`), 10 px-es lekerekítés, saját árnyék, a három gomb fölé víve megjelenik a ×, − és + jel.
- Indítás: Start-menü / Spotlight → "Terminal" (parancsikon: Start Menu\Programs\Terminal.lnk), vagy `terminal\Terminal.exe`.
- **Turbó-csomag (240+ funkció, lista: `terminal\FEATURES.md`, a parancspaletta műveleti jegyzékéből generálva):**
  1. Lapok + osztott panelek (Ctrl+Shift+T / D / E, húzható elválasztó, Ctrl+Shift+Z kinagyítás, lap-átnevezés/szín/áthúzás)
  2. Parancspaletta (Ctrl+Shift+P): minden funkció fuzzy kereséssel, gyorsbillentyűk átírása: `terminal\keys.ini` (`azonosító=Ctrl+Shift+X`)
  3. Fuzzy előzmény-kereső (Ctrl+R): PowerShell-előzmény + nyitott lapok parancsai; Enter beír, Shift+Enter lefuttat
  4. Parancs-blokkok: ✓/✗ + idő jelvény minden parancs mellett, hibánál piros sáv; Ctrl+Fel/Le ugrás a parancsok közt; utolsó kimenet másolása (Ctrl+Shift+O)
  5. Kattintható linkek és `fájl:sor:oszlop` útvonalak (böngésző / VS Code / Antigravity / Intéző)
  6. Keresés a teljes görgetősávban (Ctrl+F, regex, kis/nagybetű)
  7. Beillesztés-védelem: többsoros szöveg jóváhagyással, okos idézőjelek javítása, "egy sorba" / "útvonalként" mód
  8. Quake mód: `Terminal.exe /quake` (vagy `quake=1` a term.ini-ben) → globális Ctrl+Alt+Space (`quakekey=`) lenyíló terminál
  9. Profilok (PowerShell, pwsh, cmd, Git Bash, WSL, Python, Node, SSH-gazdák, saját `profile.N=név|parancs|mappa`) + lap-visszaállítás induláskor
  10. Hosszú parancs értesítés (alapból 10 mp felett): tálcavillogás + buborék + lapjelzés
  11. **Élő előnézet a terminál alatt** (Edge/WebView2 motor, a `terminal\webview2` mappa DLL-jeivel): ha egy futó parancs kimenetében localhost-cím jelenik meg (Vite, Next, Express…), jobb alul felugrik: „📺 Előnézet alul” / „↗ Böngészőben”. Kézzel: `preview 5173` (vagy `preview localhost:3000`) a shellben, Ctrl+Shift+J, vagy a link helyi menüjében. Eszköztár: vissza/előre/újratöltés, címsor, 💻/▭/📱 szélesség (teljes / 820 / 390 px), 🛠 DevTools, ↗ böngésző, ✕. A vágólap-szerű linkek (http…) mostantól sima kattintásra megnyílnak a böngészőben (`linkctrl=1` a term.ini-ben visszaadja a Ctrl+kattintást). A Dev Hub Portok lapján minden sor mellett „📺 Terminal preview” gomb van.
  + 14 téma, snippetek (`snippets.txt`, `{ask:…}`, `{clip}`, `{cwd}`), vágólap-előzmény, broadcast mód, napló, export (txt/HTML/PNG), egérjelentés, bracketed paste, OSC 7/9/52/133, PowerShell-segédparancsok (`lumen help`).
- Egyéb billentyűk: Ctrl+Shift+C = másolás, Ctrl+V / jobb klikk = beillesztés, Ctrl+gördítés vagy Ctrl+Plus/Minus/0 = betűméret, Shift+PgUp/PgDn = görgetés, Ctrl+Shift+N = új ablak, Ctrl+Shift+K = törlés, Alt+nyilak = panelváltás, Ctrl+1..9 = lap, Ctrl+Tab = következő lap, F11 = teljes méret, Ctrl+Shift+F1 = gyorsbillentyű-lista.
- Beállítások: `terminal	erm.ini` (téma, betű, kurzor, átlátszóság, értesítés… – a palettából is kapcsolhatók; shell, cols, rows, closeonexit, startdir, quake, quakekey, quakeheight). Fordítás: `terminal\Build.ps1`. Tesztkapcsolók: `/shot f.png`, `/dump f.txt`, `/exec "parancs"`, `/run akció,akció`, `/after ms`, `/features f.md`.
- Ismert korlát: Windows PowerShell 5.1 beépített PSReadLine-ja az AltGr-os karaktereket (Ű, €, ł) nem fogadja el ConPTY-n át; a cmd.exe és a többi program igen. PowerShell 7 (pwsh) telepítésével megoldódik, utána `shell=pwsh`.

## GlassControl (macOS Terminal stílus)
- A `GlassControl.exe` (Memória-sepregető, Folyamatok, Glass UI, Fejlesztői Hub, Terminál) át lett stilizálva: semleges szénszürke, rendszerkék/zöld kiemelések, Terminal-szerű címsor és fülek.
- Terminál fül: ugyanaz a turbózott terminál (lapok, panelek, paletta…), mint a Terminal.exe.
- Folyamatok fül: "Leállítás" és "RAM ürítés" gomb a kijelölt folyamatra (leállításnál megerősítés).
- **Fejlesztői Hub (kattintásos, 9 lap; a hosszabb műveletek a Terminál fül új lapján futnak, így látszanak és megszakíthatók):**
  1. 🧰 Eszközök: 29 fejlesztői eszköz (Python, Node, Java, Go, Rust, .NET, Deno, Bun, pnpm, Yarn, uv, Git, gh, VS Code, Docker, PowerShell 7, CMake, jq, FFmpeg, Postman, Notepad++, 7-Zip…) – valós verzió-ellenőrzés (a frissen telepítettek újraindítás nélkül is látszanak), egy kattintásos Telepítés / Frissítés / Eltávolítás, csoportos telepítés pipákkal, „Minden frissítése”, telepítés közbeni automatikus állapotfigyelés
  2. 🔎 Winget: keresés a teljes winget-katalógusban, telepítés kattintásra
  3. 📁 Projektek: automatikus keresés a gépen (git / Node / Python / .NET / Rust / Go / Java), típus- és csomagkezelő-felismerés, git-állapot (ág, módosítások, ↑↓), VS Code / Antigravity / Intéző / Terminál egy kattintásra, package.json-szkriptek gombként, Python venv/pip, dotnet/cargo/go/mvn/gradle/compose parancsok, Pull/Push/Fetch/Commit/Ág váltás/Stash, build-mappák méretezése és törlése
  4. ✨ Új projekt: 13 sablon (React/Vite, Next.js, Vue, Svelte, weboldal, Express, Python, FastAPI, .NET, Rust, Go), git init + megnyitás a szerkesztőben
  5. 🔌 Portok: élő lista (3 mp), ki használja a portot, megnyitás böngészőben, URL másolása, folyamat leállítása, „port felszabadítása”
  6. 🌐 Környezet: PATH-szerkesztő (hibás/duplikált elemek jelölése, tisztítás, sorrend; UAC nélkül, automatikus mentéssel a `devhub\` mappába), környezeti változók, git név/e-mail/beállítások, SSH-kulcs készítés és másolás
  7. ⚡ Gyors: 40+ szöveg-eszköz (kis/nagybetű, camel/snake/kebab, JSON szépítés + hibahely, Base64/URL/HTML, JWT, hash-ek, UUID, jelszó-generátor, epoch↔dátum, szín, regex-teszt…) – eredmény automatikusan a vágólapra
  8. 🧹 Takarítás: npm/pnpm/yarn/pip/NuGet/Gradle/Maven/Cargo/Go/VS Code cache + Temp méretei és törlése, projektek node_modules/build-mappái, docker prune
  9. 🐳 Docker: konténerek (start/stop/restart/napló/shell/törlés), képek
  Megjegyzés: a winget a csomagforrást a `cdn.winget.microsoft.com`-ról tölti; ha az nem érhető el (hálózat/tűzfal/VPN), a Hub ezt jelzi és megnyitja a gyártó letöltő oldalát. Fájlok: `DevHub.cs`, `DevHubProjects.cs`, `DevHubMisc.cs`; adatok: `devhub\`.
- Fordítás: `Build-GlassControl.ps1`. Az eredeti forrás mentése: `backup-glasscontrol\`.

## Spotlight – Alt+Space (vagy Win+S)
- macOS Spotlight-szerű keresősáv a GlassDockban: alkalmazások (a Start-menü listája, a saját hozzáadottakkal), rendszer-műveletek (Beállítások, Feladatkezelő, Parancssor, Zárolás), számolás (pl. `12*7+3`, Enter = másolás), fájlok (Windows Search index) és webes keresés.
- Fel/le nyíl vagy Tab = lépkedés, Enter = indítás, Esc vagy kattintás kívülre = bezár. A gyakrabban indított alkalmazások előrébb kerülnek (`dock\spotlight-usage.txt`).
- Kikapcsolás: `dock\settings.ini` → `spotlight=0`, dokk újraindítása.
- Az új telepítések maguktól megjelennek a Start-menüben és a Spotlightban is (a Start Menu mappákat figyeli).

## GlassControl – iOS Glass UI fül (az üveghatás vezérlőpultja)
- Csúszkák, színválasztó (árnyalat/telítettség/világosság + hexa + minta-körök), betűtípus-lista keresővel, kapcsolók, 9 előbeállítás és élő előnézet. Minden a `dock\settings.ini`-be íródik, a GlassDock ~0,3 mp-en belül élőben átveszi (dokk, pilulák, Start-menü, Spotlight; a felugróknál a következő megnyitáskor).
- Állítható: üveg sűrűsége, elmosás ereje, háttér telítettsége és fényereje, fényes felső sáv, keret erőssége és színe, árnyalat színe, kerekítés (dokk, menük, ikon-csempék, felugrók, felugrók távolsága), betűtípus, betűméret, vastagság, csempe-üveg erőssége, ikonok fényereje, animáció-sebesség, fekete-fehér ikonok, automatikus elrejtés, pilulák, Start gomb, Spotlight stb.
- Egy csúszka nevére duplán kattintva az alapérték áll vissza; a "Minden alapértékre" gomb mindent visszaállít.
- A kulcsok leírása: `dock\Theme.cs`. A beállításfájlt a dokk és a GlassControl egyszerre használja (háromutas összefésülés).

## GlassControl – Analitika fül
- 5 mp-enként méri a rendszer memóriáját és a programok (név szerint összevont) RAM-használatát; háttérszálon fut, amíg a GlassControl nyitva van.
- Mutatja: átlag (munkamenet és hosszú táv), csúcs és ideje, élő grafikon (10 perc / 1 óra / minden) a Wise Wiper tisztítások jelölésével, programonkénti Most/Átlag/Csúcs/Mikor/db/Trend, magyarázat kattintásra (mi ez, mikor és mennyi, trend, tipp), napszak-profil (24 óra) és a Wiper napló.
- Naplók: `analytics-log.csv` (percenként, 14 napra), `analytics-wiper.csv`.
- A Wise Memory Wiper eredménye az "Elérhető" kártya alatt jelenik meg (nincs külön ablak), a felszabadítás mérése javítva.

## Jobb klikkes (üveg) menük mindenhol
- Közös stílus (`terminal\GlassMenu.cs`): sötét üveg, lekerekített menü, kék kiemelés, almenük, pipák. GlassControl, Terminal és GlassDock is ezt használja (a dokk meglévő menüi is ilyenek lettek).
- GlassControl: fejléc/fülsáv (mindig felül, teljes méret, háttérbe, indítás a Windows-szal, mappák, kilépés), Wiper (optimalizálás, automatikus seprés, küszöb, gyakoriság, okos mód), Folyamatok (RAM ürítés, leállítás, összes ilyen leállítása, prioritás, útvonal megnyitása/másolása, keresés), Analitika (rendezés, időtáv, tanulás most, memory.txt; soronként: ürítés, leállítás, útvonal, összefoglaló), iOS Glass UI (előbeállítások, alapérték, dokk újraindítása), Fejlesztői Hub, szövegmezők (kivágás/másolás/beillesztés).
- Terminal: ablak címsora (új ablak, teljes méret, mindig felül, betűméret…) és a terminál területe (másolás, beillesztés, összes kijelölése, törlés, betűméret, új ablak).
- A piros gomb a GlassControlban a tálcára küldi (a mérés és az automatikus seprés tovább fut); teljes kilépés: jobb klikk a fejlécen → Kilépés, vagy a tálca ikon menüje.

## Analitika mappa és tanulás – `analytics\`
- `raw\minutes.csv`: percenkénti nyers mérés (35 napig), benne az előtérben lévő program is; `raw\wiper.csv`: a seprések (programonkénti felszabadítással).
- Ciklus: a mérés első napja az 1. nap; minden hét 3 szakaszra oszlik: `1-2_day`, `3-4_day`, `5-6-7_day`. Egy szakasz lezárultakor a hozzá tartozó mappába kerül egy txt (részletes: órák, programok, munkamenetek, wiper, átlagok).
- `summa_month\summa_1..4`: heti mappák, bennük 1 txt a 3 szakasz rövid, tömör összegzőjével (a sorok végén a `##` utáni rész gépi adat a tanuláshoz).
- A 4. hét végén: `summa_month\mont_N\mont_N.txt` (a 4 heti összegző összegzése, előző hónaphoz hasonlítással) és a nagy `summa_month\months.txt` kap egy szakaszt, majd a `summa_1..4` mappák törlődnek.
- `memory\memory.txt`: egy nagy, soha nem törlődő txt, szakaszokra bontva (GLOBAL, PROGRAM <név>…, SWEEP-POLICY, PREDICTION, PROCESSED FILES). Naponta egyszer (és "Tanulás most") beolvassa az összes txt-t, és frissíti: mi mennyit használ, mikor indul, mennyi ideig fut, mennyit van az előtérben, mennyit szabadít a seprés.
- Okos seprés (a tanultból): az előtérben lévő programot kihagyja; a háttérben felfúvódó nagy programokat előre veszi; kihagyja, ahol eddig alig volt nyereség; a gyakran használt kisebb programokhoz nem nyúl; ha az adott óra tanultan nehéz, kicsit előbb indul.
- Automatikus seprés (Wiper fül, jobb klikk): alapból BE, 50% felett, legfeljebb 10 percenként (`analytics\autosweep.ini`).

## Wise Memory Wiper – Haladó beállítások és háttérben futás
- A Wiper fülön a "Haladó beállítások" gomb nyitja: előbeállítások (Óvatos, Kiegyensúlyozott, Agresszív, Csak időzítve, Játék közben csendben, Ki), indítási feltételek (használt memória %, szabad memória GB alatt, időzítés percenként, két seprés közti minimum, napi időablak, csak tétlenül), mit seperjen (okos mód, előtér-védelem, minimális folyamatméret, kizárt programok listája), háttérben/indításkor (indítás a Windows-szal, rejtve indulás, a piros gomb háttérbe küld, seprés az indítás után, értesítés).
- A beállítások az `analytics\autosweep.ini` fájlban vannak. A seprés akkor is fut, ha az ablak zárva (a tálcán van): ehhez kapcsold be az "Indítás a Windows-szal" és az "Indításkor rejtve" opciót.
- iOS Glass UI fül: amíg ez a fül aktív, a dokk és a tálca-pilulák nem tűnnek el (a GlassControl 1,5 mp-enként frissíti a `dock\pin.txt`-t); a fül elhagyása után a megszokott módon elrejtőznek.

## Lumen (a GlassControl új neve) és telepítés
- A vezérlőközpont neve **Lumen** (a fejléc jobb oldalán a LUMEN felirat), saját ikonnal (`Lumen.ico`, generálja: `Make-LumenIcon.ps1`). A fájl neve továbbra is `GlassControl.exe`.
- Az első indításkor "települ": parancsikon az asztalra és a Start-menübe (a Start-keresés és a Spotlight is megtalálja: "Lumen"), a régi "GlassControl Suite" parancsikonok takarítása. Egyszeri (`install.ini` jelzi). Újra létrehozás: jobb klikk a fejlécen → Parancsikonok újralétrehozása, vagy `GlassControl.exe /install`; eltávolítás: `GlassControl.exe /uninstall`.
- Indítási kapcsolók: `/tray` (rejtve indul, csak tálca ikon), `/install`, `/install-only`, `/uninstall`.

## Betűtípus és ikon-átfedések
- **Betűtípus:** az Apple iOS-fontja (San Francisco) csak Apple-platformra használható, ezért a hozzá legjobban hasonlító nyílt betűtípust, az [Inter](https://github.com/rsms/inter)-t (OFL) használom. Telepítve csak a te felhasználódnak (`%LOCALAPPDATA%\Microsoft\Windows\Fonts`). Az asztali ikonok feliratai és a dokk szövegei (tooltip, előnézeti kártyák, menü) ezt használják. Visszaállítás: `fonts\Restore-IconFont.ps1`.
- **Parancsikon-nyíl és UAC-pajzs eltüntetése:** `dock\Hide-ShortcutOverlays.ps1` (egyszer UAC-ot kér, mert gépszintű beállítás). Visszaállítás: `dock\Restore-ShortcutOverlays.ps1`.

## taskbar-ios\ — a Windows tálca rendbetétele + a dokk indítása
- `Apply-Taskbar.ps1` – tálca: 40 px, eszköztárak nélkül, automatikus elrejtésű (a dokk vezérli), nincs a 2. monitoron; elindítja a GlassDock-ot. A fájl tetején állítható; `$UseDock = $false` esetén a TaskbarX-es pilulát használja.
- `Install-Taskbar.ps1` – bejelentkezéskor induló feladat (`-Uninstall` = eltávolítás).
- Eszköz (opcionális): [TaskbarX](https://github.com/ChrisAnd1998/TaskbarX), `tools\TaskbarX`.

## startup\ — indításkor (még NINCS telepítve)
- `Set-WiseOptimizer.ps1` – Wise Memory Optimizer: optimalizál, ha a használat eléri az 50%-ot (elérhető < 50%).
- `Start-WallpaperEngine.ps1` – a Wallpaper Engine aktuális hátterét előre beállítja Windows-háttérnek, majd `-silent` módban indítja.
- `Install-Startup.ps1` – feladatok regisztrálása (UAC-ot kér; `-Uninstall` = törlés).

## Súgó (Lumen → 📖 Súgó fül)
- Fa-nézetű "vault": kártyák, összecsukható ágak, nagyítás/húzás, keresés; Lista / Ötletek / Jegyzetek nézet. Kártyára kattintva részletek + „Jegyzeteim”; sárga jegyzetek (húzhatók, átméretezhetők, kártyához köthetők), saját ötletek („+ Új”). Mentés: `help\notes.txt`.
- Újdonságok: Titok-őr (terminál), Folyamat-boncolás (feladatkezelő jobb klikk), Fókusz-mód + memória-előrejelzés (wiper oldal), Spotlight-akciók (`port 3000`, `ram`) és vágólap-érzékeny javaslatok.

## Nyelv / Language / Sprache
- Alapnyelv: **English**. Súgó fül → fejléc → 🌐 gomb: English / Magyar / Deutsch. A beállítás: `dock\language.txt` (`en` / `hu` / `de`); a dokk, Spotlight és Start-menü ~1 mp-en belül átveszi, a GlassControl és a Terminal újraindítás után (a gomb felajánlja).
- A fordítások: `lang\lang.tsv` (magyar kulcs <TAB> angol <TAB> német), a build beágyazza az exe-be; a kódban `Loc.T("magyar szöveg")` hívások vannak (`Loc.cs`). Hiányzó fordításnál a magyar szöveg látszik.

