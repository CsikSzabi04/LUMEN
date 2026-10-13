# 🍏 GlassControl Suite — Windows 10 iOS Üveghatású Rendszer- és Memória-Optimalizáló

> **Nyílt forráskódú (Open-Source) Windows 10/11 Rendszeroptimalizáló és iOS Glass UI Kezelőközpont**
> 
> Magas memóriahasználat megszüntetése, intelligens RAM-sepergetés, saját beépített teljesítmény-látó (Feladatkezelő), valamint a Windows 10 asztal átalakítása iOS-szerű üveghatású dokkal és tálcával — **egyetlen kattintással vagy CMD parancssorból**.

---

## 📸 Áttekintés & Fő Funkciók

A **GlassControl Suite** egy egybefüggő, modern, sötét módú és iOS-stílusú üveghatású (Glassmorphism) alkalmazásfelület, ami egyesíti a gép teljesítmény-optimalizálását és az asztali környezet testreszabását.

```
+-----------------------------------------------------------------------------------+
|                               GLASSCONTROL SUITE                                  |
+-----------------------------------------------------------------------------------+
| [📊 Teljesítmény Látó]  [🧹 Wise Memory Wiper]  [🍏 iOS Glass UI]  [⚙️ Indítópult] |
+-----------------------------------------------------------------------------------+
|                                                                                   |
|  • CPU, RAM, SSD, GPU Élő Grafikonok (Windows Task Manager stílusban)              |
|  • Intelligens 5-10 perces RAM Sepergető & Munkakészlet Ürítő                     |
|  • GlassDock, iOS Tálca Pilulák és GlassStart Menü Vezérlőközpont                 |
|  • Asztali Ikonok és Parancsikon-Nyilak 1-Kattintásos Kezelése                    |
|                                                                                   |
+-----------------------------------------------------------------------------------+
```

---

## 🛠️ Modulok és Részletes Működés

### 1. 🧹 Wise Memory Wiper & RAM Sepergető
* **Élő Memóriafogyasztás Mérés**: Fizikai RAM, Tömörített memória, Standby gyorsítótár és Elérhető memória valós idejű kijelzése.
* **Azonnali 1-Kattintásos Sepergetés**: Kiüríti a futó alkalmazások munkakészletét (`EmptyWorkingSet`) és a Windows Standby gyorsítótárát (`ClearStandbyList`).
* **Intelligens Automatizált Háttérfigyelő**:
  - **5 percenként**: Részletes naplózás a `memoria_naplo.txt` fájlba magyar nyelvű leírásokkal (*Mi ez*, PID, MB, RAM %).
  - **10 percenként**: A naplófájl automatikus törlése és frissítése (nem nő végtelenre a fájlméret).
  - **Beállítható Küszöbérték**: Automatikus szivárgás-megszüntetés pl. 75-80% feletti terhelésnél.
* **Védelmi Szabályok**: Soha nem bántja a rendszerkritikus folyamatokat (`System`, `svchost.exe`, `dwm.exe`, `MsMpEng.exe`), és védi az épp aktív előtéri ablakot a lassulástól.

### 2. 📊 Saját Feladatkezelő & Teljesítmény Látó (Task Manager)
* **Élő Teljesítmény Grafikonok**:
  - **CPU**: Terhelés %, órajel, magok állapota.
  - **Memória**: Használt/Elérhető RAM, Gyorsítótárazott (Standby), Lapozható és Nem-lapozható készlet.
  - **Lemez (C: / D:)**: Olvasási és írási sebesség (MB/s), válaszidő és SSD/HDD terheltség.
  - **Hálózat**: Élő feltöltési és letöltési sebesség (Kb/s / Mb/s).
  - **GPU**: Grafikus kártya terhelés % és hőmérséklet (AMD / NVIDIA támogatással).
* **Folyamatkezelő Táblázat**:
  - Rendezhető folyamatlista memóriahasználat, CPU és PID alapján.
  - Részletes magyar megnevezések a futó programokhoz (pl. Brave, Chrome, VS Code, Discord, Steam).
  - **Műveletek**: Feladat befejezése (Process Kill), Egyedi memóriatisztítás, Elérési út megnyitása.

### 3. 🍏 iOS Glass UI & Asztali Tükörhatás Kezelő
* **GlassDock Vezérlés**:
  - Dokk indítása / leállítása / újraindítása.
  - Üveghatás sötétsége, áttetszősége és lekerekítési sugár csúszkái.
  - Fekete-fehér iOS üveg csempék kapcsolása.
  - Automatikus elrejtés és monitor beállítások.
* **iOS Tálca és Pilulák (Taskbar-iOS)**:
  - Windows tálca gombjainak elrejtése és lebegő iOS üveg pilulák bekapcsolása.
  - GlassStart menü engedélyezése/tiltása (iOS stílusú kereső és alkalmazásrács).
* **Asztali Ikonok és Rendszer-Testreszabás**:
  - Parancsikon-nyilak és UAC kis pajzsok 1-kattintásos eltüntetése / visszaállítása.
  - Asztali ikonok elrendezésének mentése és visszaállítása (`Restore-DesktopLayout.ps1`).
  - iOS Inter betűtípus alkalmazása az asztali ikonokra (`Restore-IconFont.ps1`).

### 5. 💻 Interaktív Beépített Terminál (Developer Console)
* Beépített sötét módu fejlesztői konzol (CMD / PowerShell szimuláció).
* Rendszer-parancsok, Python scriptek és fejlesztői eszközök futtatása közvetlenül a GUI felületen belül.
* Élő kimenet (streamed logs) színes szintaxis-kiemeléssel és törlési/másolási lehetőségekkel.

### 6. 🛠️ Fejlesztői Környezet & Csomagkezelő (Dev Environment Hub)
* Kifejezetten fejlesztőknek szánt **1-kattintásos telepítő és verzió-ellenőrző**:
  - 🐍 **Python**: Környezet és `uv` / `pip` csomagkezelő ellenőrzése és telepítése.
  - ⚡ **Node.js & pnpm / npm**: JavaScript/TypeScript fejlesztői eszközök 1-kattintásos telepítése.
  - ☕ **Java (JDK / OpenJDK)**: Java fejlesztői környezet beszerezése.
  - 🐙 **Git & GitHub CLI**: Verziókövető eszközök.
* Rendszergazdai jogú csendes telepítések `winget` és hivatalos csomagkezelők segítségével, élő konzol visszajelzéssel.

---

## 🖥️ Design Rendszer: Obsidian + GitHub Dark + Admin Console

A felület dizájnja a beküldött minták alapján az alábbi prémium elemekből áll:
* **Alap Színtéma**: Mély Obsidian fekete (`#0D1117` / `#090D16`) finom GitHub-sötét kártyákkal (`#161B22`).
* **iOS Glassmorphism Effect**: Áttetsző, elmosódott üveg panelek (`backdrop-filter: blur(16px)`), hajszálvékony GitHub szegélyekkel (`1px solid #30363D`).
* **Neonszín Akcentusok & Jelvények**:
  - 🟣 **Obsidian Violet (`#8B5CF6`)**: Elsődleges akció gombok és aktív fülek.
  - 🔷 **Cyber Cyan (`#38BDF8`)**: CPU, RAM és hálózati élő mérőműszerek.
  - 🟢 **Emerald Green (`#10B981`)**: Optimális memóriaállapot és sikeres akciók.
  - 🟡 **Amber Gold (`#F59E0B`)**: Közepes terhelés és figyelmeztetések.
  - 🔴 **Crimson Red (`#EF4444`)**: Kritikus RAM/CPU terhelés és folyamat leállítás.
* **Admin Kontroll Pult**: Gamified / Cyberpunk stílusú Adminisztrátori rendszer-szintű fejléccel (LVL / System Status / RAM Shield), élő progress ringekkel és JetBrains Mono típusú precíziós számkijelzőkkel.

---

## ⚡ Indítás 1-Kattintással (.EXE vagy CMD)

1. **Önálló `.EXE` Fájllal (Ajánlott)**:
   Duplakattintás a `GlassControl.exe` fájlra a mappában.
2. **CMD Parancssorból**:
   ```cmd
   d:\ADVANCED_USE\start.bat
   ```
3. **Pythonból közvetlenül**:
   ```cmd
   py app.py
   ```

*Megjegyzés: Az alkalmazás indításakor automatikusan rendszergazdai (Admin) jogosultságot kér a memóriatisztítási API-k, csomagtelepítők és a tálca beállítások eléréséhez.*

---

## 📦 GitHub Repository Szerkezet

```
ADVANCED_USE/
├── GlassControl.exe            # Önálló fordított Windows alkalmazás indító (.exe)
├── start.bat                   # 1-kattintásos CMD indító fájl
├── app.py                      # Fő alkalmazás GUI (Obsidian Glass Suite Szerver & Felület)
├── memory_monitor.py           # Wise Memory Wiper háttérmotor és naplózó
├── memoria_naplo.txt           # Automatikus 5/10 perces RAM napló
├── application.md              # Részletes dokumentáció és specifikáció (GitHub README)
├── README.md                   # Rendszer leírás és eredeti komponens útmutató
├── dock/                       # GlassDock C# forráskód, exe és beállítások
├── taskbar-ios/                # iOS tálca és pilula PowerShell szkriptek
├── startup/                    # Auto-start feladatütemező szkriptek
└── fonts/                      # Inter iOS betűtípus telepítő és registry beállítások
```

---

## 📜 Licenc & Szerzői Jogok

Nyílt forráskódú projekt. Szabadon felhasználható, módosítható és terjeszthető a GitHub közösség számára.
