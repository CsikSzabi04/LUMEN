# Lumen Terminal – funkciók

A lista a parancspaletta (Ctrl+Shift+P) valódi műveleti jegyzékéből készül, így mindig naprakész. Gyorsbillentyűk: `keys.ini` (`azonosító=Ctrl+Shift+X`).

## Nagy újítások (amit a sima cmd nem tud)

1. Lapok és osztott panelek – több shell egy ablakban, húzható elválasztók, panel-kinagyítás, lap-átnevezés/színezés/átrendezés, bezárt lap újranyitása
2. Parancspaletta (Ctrl+Shift+P) – minden funkció egy helyen, fuzzy kereséssel; a gyorsbillentyűk a keys.ini-ben átírhatók
3. Fuzzy előzmény-kereső (Ctrl+R) – a PowerShell teljes előzményében és az összes nyitott lap parancsaiban keres, Shift+Enter azonnal futtat
4. Parancs-blokkok – minden parancs mellett ✓/✗ jelvény + futási idő, hibánál piros sáv; Ctrl+Fel/Le ugrás a parancsok között, az utolsó kimenet egy kattintással másolható/kijelölhető, a parancs újrafuttatható
5. Kattintható linkek és fájlútvonalak – URL a böngészőben, fájl:sor:oszlop a szerkesztőben (VS Code / Antigravity), mappa az Intézőben, relatív útvonalak a munkamappához képest
6. Keresés a teljes görgetősávban – találat-számláló, kis/nagybetű és regex kapcsoló, F3 / Shift+F3
7. Beillesztés-védelem – többsoros szöveg beillesztése előtt jóváhagyás, okos idézőjel/kötőjel javítás, egy sorba / idézőjelezett útvonalként beillesztés
8. Quake mód – globális gyorsbillentyűvel (Ctrl+Alt+Space) a képernyő tetejéről lecsúszó terminál
9. Profilok és munkamenet-visszaállítás – PowerShell, pwsh, cmd, Git Bash, WSL, Python, Node, SSH-gazdák automatikusan; induláskor a lapok és mappák visszatérnek
10. Élő előnézet a terminál alatt – localhost-os cím (Vite, Next, Express…) felismerése, „Előnézet alul” egy kattintással vagy `preview 5173` paranccsal; valódi Edge-motor, DevTools, telefon/tablet nézet
11. Értesítés hosszú parancsra – ha a parancs elfut, miközben máshol dolgozol: villogó tálcagomb + buborék, lap-jelzés, a végén idő és kilépési kód

## Műveletek a parancspalettában

### Lapok

- Új lap  — `Ctrl+Shift+T`  *(tab.new)*
- Lap duplikálása (ugyanabban a mappában)  *(tab.duplicate)*
- Lap bezárása  *(tab.close)*
- Többi lap bezárása  *(tab.closeothers)*
- Jobbra lévő lapok bezárása  *(tab.closeright)*
- Bezárt lap újranyitása  — `Ctrl+Shift+U`  *(tab.reopen)*
- Következő lap  — `Ctrl+PageDown`  *(tab.next)*
- Előző lap  — `Ctrl+PageUp`  *(tab.prev)*
- Ugrás a(z) 1. lapra  — `Ctrl+1`  *(tab.select1)*
- Ugrás a(z) 2. lapra  — `Ctrl+2`  *(tab.select2)*
- Ugrás a(z) 3. lapra  — `Ctrl+3`  *(tab.select3)*
- Ugrás a(z) 4. lapra  — `Ctrl+4`  *(tab.select4)*
- Ugrás a(z) 5. lapra  — `Ctrl+5`  *(tab.select5)*
- Ugrás a(z) 6. lapra  — `Ctrl+6`  *(tab.select6)*
- Ugrás a(z) 7. lapra  — `Ctrl+7`  *(tab.select7)*
- Ugrás a(z) 8. lapra  — `Ctrl+8`  *(tab.select8)*
- Ugrás a(z) 9. lapra  — `Ctrl+9`  *(tab.select9)*
- Lap mozgatása balra  — `Ctrl+Shift+PageUp`  *(tab.moveleft)*
- Lap mozgatása jobbra  — `Ctrl+Shift+PageDown`  *(tab.moveright)*
- Lap átnevezése…  *(tab.rename)*
- Ugrás lapra… (lista)  — `Ctrl+Shift+G`  *(tab.switch)*
- Lap színe: Piros  *(tab.color.0)*
- Lap színe: Narancs  *(tab.color.1)*
- Lap színe: Sárga  *(tab.color.2)*
- Lap színe: Zöld  *(tab.color.3)*
- Lap színe: Kék  *(tab.color.4)*
- Lap színe: Lila  *(tab.color.5)*
- Lap színe: nincs  *(tab.color.none)*
- Lapsáv mindig látszik  *(tab.always)*

### Profil

- Új lap: Windows PowerShell  *(profile.Windows PowerShell)*
- Új lap: Parancssor (cmd)  *(profile.Parancssor (cmd))*
- Új lap: Git Bash  *(profile.Git Bash)*
- Új lap: WSL: Copyright (c) Microsoft Corporation. All rights reserved.  *(profile.WSL: Copyright (c) Microsoft Corporation. All rights reserved.)*
- Új lap: WSL: Usage: wsl.exe [Argument]  *(profile.WSL: Usage: wsl.exe [Argument])*
- Új lap: WSL: Arguments:  *(profile.WSL: Arguments:)*
- Új lap: WSL: --install <Options>  *(profile.WSL: --install <Options>)*
- Új lap: WSL: Install Windows Subsystem for Linux features. If no options are specified,  *(profile.WSL: Install Windows Subsystem for Linux features. If no options are specified,)*
- Új lap: WSL: the recommended features will be installed along with the default distribution.  *(profile.WSL: the recommended features will be installed along with the default distribution.)*
- Új lap: WSL: To view the default distribution as well as a list of other valid distributions,  *(profile.WSL: To view the default distribution as well as a list of other valid distributions,)*
- Új lap: WSL: use 'wsl --list --online'.  *(profile.WSL: use 'wsl --list --online'.)*
- Új lap: WSL: Options:  *(profile.WSL: Options:)*
- Új lap: WSL: --distribution, -d [Argument]  *(profile.WSL: --distribution, -d [Argument])*
- Új lap: WSL: Specifies the distribution to be downloaded and installed by name.  *(profile.WSL: Specifies the distribution to be downloaded and installed by name.)*
- Új lap: WSL: Arguments:  *(profile.WSL: Arguments:)*
- Új lap: WSL: A valid distribution name (not case sensitive).  *(profile.WSL: A valid distribution name (not case sensitive).)*
- Új lap: WSL: Examples:  *(profile.WSL: Examples:)*
- Új lap: WSL: wsl --install -d Ubuntu  *(profile.WSL: wsl --install -d Ubuntu)*
- Új lap: WSL: wsl --install --distribution Debian  *(profile.WSL: wsl --install --distribution Debian)*
- Új lap: WSL: --inbox  *(profile.WSL: --inbox)*
- Új lap: WSL: Install the optional Windows feature instead of the version available via the Microsoft Store.  *(profile.WSL: Install the optional Windows feature instead of the version available via the Microsoft Store.)*
- Új lap: WSL: --no-distribution  *(profile.WSL: --no-distribution)*
- Új lap: WSL: Do not install a distribution (cannot be used with --distribution).  *(profile.WSL: Do not install a distribution (cannot be used with --distribution).)*
- Új lap: WSL: --no-launch, -n  *(profile.WSL: --no-launch, -n)*
- Új lap: WSL: Do not launch the distribution after install.  *(profile.WSL: Do not launch the distribution after install.)*
- Új lap: WSL: --web-download  *(profile.WSL: --web-download)*
- Új lap: WSL: Download the most recent version of WSL from the internet instead of the Microsoft Store.  *(profile.WSL: Download the most recent version of WSL from the internet instead of the Microsoft Store.)*
- Új lap: WSL: --list, -l [Options]  *(profile.WSL: --list, -l [Options])*
- Új lap: WSL: Lists distributions.  *(profile.WSL: Lists distributions.)*
- Új lap: WSL: Options:  *(profile.WSL: Options:)*
- Új lap: WSL: --online, -o  *(profile.WSL: --online, -o)*
- Új lap: WSL: Displays a list of available distributions for install with 'wsl --install'.  *(profile.WSL: Displays a list of available distributions for install with 'wsl --install'.)*
- Új lap: WSL: --status  *(profile.WSL: --status)*
- Új lap: WSL: Show the status of Windows Subsystem for Linux.  *(profile.WSL: Show the status of Windows Subsystem for Linux.)*
- Új lap: WSL: --help  *(profile.WSL: --help)*
- Új lap: WSL: Display usage information.  *(profile.WSL: Display usage information.)*
- Új lap: Python  *(profile.Python)*
- Új lap: Node.js  *(profile.Node.js)*

### Panelek

- Osztás jobbra (új panel)  — `Ctrl+Shift+D`  *(pane.right)*
- Osztás lefelé (új panel)  — `Ctrl+Shift+E`  *(pane.down)*
- Panel bezárása  — `Ctrl+Shift+W`  *(pane.close)*
- Következő panel  *(pane.next)*
- Előző panel  *(pane.prev)*
- Panel kinagyítása / visszaállítása  — `Ctrl+Shift+Z`  *(pane.zoom)*
- Panelek egyenlő méretűre  *(pane.equal)*
- Bevitel küldése minden panelre (broadcast)  — `Ctrl+Shift+B`  *(pane.broadcast)*

### Szerkesztés

- Másolás  — `Ctrl+Shift+C`  *(edit.copy)*
- Másolás HTML-ként (színekkel)  *(edit.copyhtml)*
- Teljes kimenet másolása  *(edit.copyall)*
- Munkamappa másolása  *(edit.copycwd)*
- Beillesztés  — `Ctrl+V`  *(edit.paste)*
- Beillesztés formázás nélkül (okos idézőjelek javítva)  — `Ctrl+Shift+V`  *(edit.paste.plain)*
- Beillesztés egy sorba  *(edit.paste.oneline)*
- Beillesztés idézőjelezett útvonalként  *(edit.paste.quoted)*
- Összes kijelölése  — `Ctrl+Shift+A`  *(edit.selectall)*
- Aktuális sor kijelölése  *(edit.selectline)*
- Kijelölés megszüntetése  *(edit.deselect)*
- Vágólap-előzmény…  — `Ctrl+Shift+H`  *(edit.cliphistory)*
- Parancs-előzmények keresése…  — `Ctrl+R`  *(edit.history)*
- Snippetek…  — `Ctrl+Shift+S`  *(edit.snippets)*
- Utolsó parancs mentése snippetként…  *(edit.snippet.save)*

### Keresés

- Keresés a kimenetben…  — `Ctrl+F`  *(find.open)*
- Következő találat  *(find.next)*
- Előző találat  *(find.prev)*
- Keresősáv bezárása  *(find.close)*

### Nézet

- Betű nagyítása  — `Ctrl+Plus`  *(view.zoomin)*
- Betű kicsinyítése  — `Ctrl+Minus`  *(view.zoomout)*
- Betűméret alaphelyzet  — `Ctrl+0`  *(view.zoomreset)*
- Betűtípus: DejaVu Sans Mono  *(font.DejaVu Sans Mono)*
- Betűtípus: Consolas  *(font.Consolas)*
- Betűtípus: Cascadia Mono  *(font.Cascadia Mono)*
- Betűtípus: Courier New  *(font.Courier New)*
- Betűtípus: Lucida Console  *(font.Lucida Console)*
- Betűtípus: Segoe UI Mono  *(font.Segoe UI Mono)*
- Sortávolság növelése  *(view.ls.up)*
- Sortávolság csökkentése  *(view.ls.down)*
- Belső margó növelése  *(view.pad.up)*
- Belső margó csökkentése  *(view.pad.down)*
- Ablak átlátszóbb  *(view.op.down)*
- Ablak áttetszőbb → tömörebb  *(view.op.up)*
- Ablak átlátszóság alaphelyzet  *(view.op.reset)*
- Kurzor: Téglalap  *(cursor.0)*
- Kurzor: Aláhúzás  *(cursor.1)*
- Kurzor: Függőleges vonal  *(cursor.2)*
- Kurzor villogása  *(cursor.blink)*
- Nem aktív panelek halványítása  *(view.dim)*
- Parancs-jelvények (idő, kilépési kód)  *(view.badges)*
- Teljes méret / visszaállítás  — `F11`  *(view.fullscreen)*
- Mindig felül  *(view.top)*

### Téma

- Téma: macOS Pro  *(theme.macOS Pro)*
- Téma: macOS Basic (világos)  *(theme.macOS Basic (világos))*
- Téma: Homebrew  *(theme.Homebrew)*
- Téma: Ocean  *(theme.Ocean)*
- Téma: Dracula  *(theme.Dracula)*
- Téma: Nord  *(theme.Nord)*
- Téma: Solarized Dark  *(theme.Solarized Dark)*
- Téma: Solarized Light  *(theme.Solarized Light)*
- Téma: Tokyo Night  *(theme.Tokyo Night)*
- Téma: One Dark  *(theme.One Dark)*
- Téma: Gruvbox Dark  *(theme.Gruvbox Dark)*
- Téma: Monokai  *(theme.Monokai)*
- Téma: Matrix  *(theme.Matrix)*
- Téma: Red Sands  *(theme.Red Sands)*
- Téma: Man Page  *(theme.Man Page)*

### Navigáció

- Görgetés az elejére  — `Ctrl+Shift+Home`  *(nav.top)*
- Görgetés a végére  — `Ctrl+Shift+End`  *(nav.bottom)*
- Egy oldal fel  — `Shift+PageUp`  *(nav.pgup)*
- Egy oldal le  — `Shift+PageDown`  *(nav.pgdn)*
- Egy sor fel  *(nav.lineup)*
- Egy sor le  *(nav.linedown)*
- Előző parancs (prompt) a görgetősávban  — `Ctrl+Up`  *(nav.prompt.prev)*
- Következő parancs (prompt)  — `Ctrl+Down`  *(nav.prompt.next)*

### Parancs-blokk

- Utolsó parancs kimenetének másolása  — `Ctrl+Shift+O`  *(block.copyout)*
- Utolsó parancs kimenetének kijelölése  *(block.selout)*
- Utolsó parancs szövegének másolása  — `Ctrl+Shift+Y`  *(block.copycmd)*
- Utolsó parancs újrafuttatása  *(block.rerun)*

### Export

- Teljes kimenet mentése szövegként…  *(export.text)*
- Teljes kimenet mentése HTML-ként (színekkel)…  *(export.html)*
- Látható képernyő mentése szövegként…  *(export.visible)*
- Képernyőkép a terminálról (PNG az asztalra)  *(export.png)*
- Képernyőkép vágólapra  *(export.pngclip)*
- Munkamenet-napló rögzítése (nyers kimenet fájlba)  *(log.toggle)*
- Naplók mappája  *(log.open)*

### Shell

- Képernyő törlése  *(sh.clear)*
- Görgetősáv törlése  *(sh.clearsb)*
- Minden törlése (képernyő + görgetősáv)  — `Ctrl+Shift+K`  *(sh.clearall)*
- Terminál alaphelyzetbe (reset)  *(sh.reset)*
- Ctrl+C küldése (megszakítás)  *(sh.ctrlc)*
- Ctrl+D küldése (EOF)  *(sh.ctrld)*
- Ctrl+Z küldése  *(sh.ctrlz)*
- Esc küldése  *(sh.esc)*
- Shell újraindítása ebben a panelben  *(sh.restart)*
- Rendszergazdai PowerShell megnyitása (UAC)  *(sh.admin)*
- Munkamappa megnyitása az Intézőben  *(sh.cwdexplorer)*
- Munkamappa megnyitása szerkesztőben (VS Code / Antigravity)  *(sh.cwdcode)*

### Előnézet

- Előnézet megnyitása alul… (localhost cím / port)  — `Ctrl+Shift+J`  *(preview.open)*
- Előnézet: utolsó észlelt dev-szerver cím  *(preview.last)*
- Előnézet bezárása  *(preview.close)*
- Előnézet újratöltése  *(preview.reload)*
- Előnézet: teljes szélesség  *(preview.desktop)*
- Előnézet: tablet (820 px)  *(preview.tablet)*
- Előnézet: telefon (390 px)  *(preview.phone)*
- Előnézet: fejlesztői eszközök (DevTools)  *(preview.devtools)*
- Előnézet megnyitása a böngészőben  *(preview.external)*

### Megnyitás

- Beállítások fájl (term.ini)  *(open.ini)*
- Gyorsbillentyű-felülírások (keys.ini)  *(open.keys)*
- PowerShell-integráció (lumen-profile.ps1)  *(open.profile)*
- Saját PowerShell-kiegészítések (lumen-user.ps1)  *(open.user)*
- Snippet-fájl (snippets.txt)  *(open.snippets)*
- PSReadLine előzmény-fájl  *(open.history)*
- A Terminal mappája  *(open.folder)*

### Beállítás

- Kijelölés automatikus másolása  *(set.autocopy)*
- Középső gomb = beillesztés  *(set.middle)*
- Többsoros beillesztés jóváhagyása  *(set.pasteconfirm)*
- Okos beillesztés (idézőjelek, kötőjelek javítása)  *(set.smartpaste)*
- Linkek megnyitása csak Ctrl+kattintással  *(set.linkctrl)*
- Értesítés hosszú parancs végén  *(set.notify)*
- Vizuális csengő (villanás)  *(set.visualbell)*
- Hangos csengő  *(set.audiblebell)*
- Görgetés a végére új kimenetnél  *(set.scrollout)*
- Görgetés a végére gépeléskor  *(set.scrollkey)*
- Lapok visszaállítása induláskor  *(set.restore)*
- Lap bezárásának megerősítése, ha fut benne valami  *(set.confirmclose)*
- Görgetősáv hossza: 5000 sor  *(set.sb.5000)*
- Görgetősáv hossza: 20000 sor  *(set.sb.20000)*
- Görgetősáv hossza: 100000 sor  *(set.sb.100000)*
- Görgetési sebesség növelése  *(set.wheel.up)*
- Görgetési sebesség csökkentése  *(set.wheel.down)*
- Értesítési küszöb: +5 mp  *(set.notify.sec)*

### Ablak

- Új ablak  — `Ctrl+Shift+N`  *(win.new)*
- Ablak minimalizálása  *(win.min)*
- Ablak bezárása  *(win.close)*
- Lenyíló (quake) mód váltása  *(win.quake)*

### Súgó

- Gyorsbillentyűk és funkciók listája  — `Ctrl+Shift+F1`  *(help.keys)*
- PowerShell segédparancsok (lumen help)  *(help.lumen)*
- Parancspaletta  — `Ctrl+Shift+P`  *(help.palette)*

## Beépített működés (nem műveletként elérhető)

- 14 beépített színtéma, élő váltással
- DejaVu Sans Mono beágyazva, ligatúra-mentes, pontos cellakirajzolás
- Truecolor (24 bit) és 256 színű paletta
- Félkövér, halvány, dőlt, aláhúzott, áthúzott, fordított, villogó karakterek
- Dupla aláhúzás és színes aláhúzás
- Teljes Unicode, széles (CJK, emoji) karakterek két cellában
- Alternatív képernyő (vim, less, htop, fzf)
- Görgetősáv húzható csúszkával
- Kurzor-forma és villogás, alkalmazás által állítható (DECSCUSR)
- Egérjelentés (1000/1002/1003, SGR 1006) – mc, htop, vim egér támogatás
- Fókusz-események (1004)
- Bracketed paste mód
- OSC 0/2 ablakcím
- OSC 7 munkamappa-követés
- OSC 9 / 777 értesítések
- OSC 52 vágólap-írás (távoli gépről is)
- OSC 133 kilépési kód
- Kijelölés: dupla kattintás szó, hármas kattintás sor, Alt+húzás téglalap
- Automatikus másolás kijelöléskor (kapcsolható)
- Középső gomb = beillesztés (kapcsolható)
- Ctrl+görgetés = nagyítás
- Sima görgetés, finomhangolható sebesség
- Fájl / mappa ráhúzása a terminálra: útvonal beillesztése idézőjelezve
- Vizuális és hangos csengő
- Inaktív panelek halványítása
- Munkamappa követése lapnévként
- Futó parancs neve a lapon
- Lap-aktivitás és csengő jelzése háttérlapon
- Broadcast – a gépelés minden panelre megy
- Munkamenet-napló fájlba (nyers kimenet)
- Export: szöveg, HTML (színekkel), PNG, vágólapra HTML/kép
- Glass kontextusmenü mindenhol
- Snippetek változókkal ({clip} {cwd} {date} {ask:…})
- Vágólap-előzmény (40 elem)
- Gyorsbillentyű-felülírás a keys.ini-ben
- Beállítások a term.ini-ben, élő újratöltés
- Saját PowerShell-kiegészítések: lumen-user.ps1
- Beépített PowerShell-segédparancsok (lumen help)

---
Összesen: 254 funkció (11 nagy újítás + 205 paletta-művelet + 38 beépített képesség).
