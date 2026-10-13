import argparse
import ctypes
from ctypes import wintypes
from datetime import datetime
from pathlib import Path
import os
import sys
import time

import psutil


# Időzítések (másodpercben)
LOG_INTERVAL = 5 * 60       # 5 perc
RESET_INTERVAL = 10 * 60    # 10 perc (napló törlése/újrakezdése)
CHECK_INTERVAL = 15         # 15 másodpercenkénti ellenőrzés
TRIM_COOLDOWN = 10 * 60     # Folyamatonként legfeljebb 10 percenkénti tisztítás
MIN_TRIM_MB = 250           # Csak legalább 250 MB RAM-ot használó folyamatokat ürít

LOG_PATH = Path(__file__).resolve().with_name("memoria_naplo.txt")

# Részletes magyar leírások a gyakori Windows 10 folyamatokhoz
DESCRIPTIONS = {
    "chrome.exe": "Google Chrome böngésző vagy annak segédfolyamata",
    "msedge.exe": "Microsoft Edge böngésző vagy annak segédfolyamata",
    "firefox.exe": "Mozilla Firefox böngésző",
    "brave.exe": "Brave böngésző",
    "opera.exe": "Opera böngésző",
    "vivaldi.exe": "Vivaldi böngésző",
    "code.exe": "Visual Studio Code szerkesztő vagy segédfolyamata",
    "antigravity.exe": "Antigravity AI szerkesztő / segédfolyamat",
    "python.exe": "Python scriptek / interpreter",
    "pythonw.exe": "Python háttérfolyamat (konzol nélkül)",
    "node.exe": "Node.js JavaScript futtatókörnyezet",
    "discord.exe": "Discord csevegő és hangkommunikációs kliens",
    "slack.exe": "Slack kommunikációs kliens",
    "teams.exe": "Microsoft Teams kliens",
    "telegram.exe": "Telegram üzenetküldő",
    "whatsapp.exe": "WhatsApp Desktop kliens",
    "steam.exe": "Steam kliens",
    "steamwebhelper.exe": "Steam böngésző / webes felület segédfolyamata",
    "epicgameslauncher.exe": "Epic Games Launcher kliens",
    "gog_galaxy.exe": "GOG Galaxy játékindító",
    "leagueclient.exe": "League of Legends kliens",
    "wallpaper32.exe": "Wallpaper Engine háttérkép motor (32-bit)",
    "wallpaper64.exe": "Wallpaper Engine háttérkép motor (64-bit)",
    "spotify.exe": "Spotify zenelejátszó",
    "vlc.exe": "VLC médialejátszó",
    "obs64.exe": "OBS Studio videófelvevő és streamelő",
    "explorer.exe": "Windows Fájlkezelő és asztali felület",
    "svchost.exe": "Windows rendszerszolgáltatások gazdafolyamata",
    "msmpeng.exe": "Microsoft Defender vírusvédelem",
    "dwm.exe": "Windows Asztali ablakkezelő (Desktop Window Manager)",
    "searchindexer.exe": "Windows keresési indexelő szolgáltatás",
    "onedrive.exe": "Microsoft OneDrive felhő szinkronizáló",
    "vmmem": "Hyper-V / Docker virtuális gép memóriája",
    "vmmemwsl": "WSL2 (Windows Subsystem for Linux) virtuális gép RAM-ja",
    "system": "Windows Rendszermag (Kernel)",
    "registry": "Windows Rendszerleíró Adatbázis memória-gyorsítótára",
    "memory compression": "Windows RAM kompressziós (tömörítési) rendszer",
    "memcompression": "Windows RAM kompressziós (tömörítési) rendszer",
    "glassdock.exe": "GlassDock asztali dokk alkalmazás",
    "language_server.exe": "Kódelemző és auto-complete segédfolyamat (Language Server)",
    "shellexperiencehost.exe": "Windows Shell Élménygazda (tálca, értesítések)",
    "startmenuexperiencehost.exe": "Windows Start menü felületgazda",
    "amdrsserv.exe": "AMD Radeon illesztőprogram segédfolyamata",
    "radeonsoftware.exe": "AMD Radeon Grafikus vezérlőszoftver",
    "runtimebroker.exe": "Windows alkalmazási engedély- és biztonsági broker",
    "mpdefendercoreservice.exe": "Microsoft Defender vírusirtó magszolgáltatás",
    "sihost.exe": "Windows Shell Infrastruktúra Gazdafolyamat",
}

# Rendszerkritikus és védett folyamatok, amiket SOHA nem szabad bántani
PROTECTED = {
    "system", "registry", "memory compression", "secure system",
    "system idle process", "smss.exe", "csrss.exe", "wininit.exe",
    "services.exe", "lsass.exe", "winlogon.exe", "svchost.exe",
    "dwm.exe", "explorer.exe", "msmpeng.exe", "searchindexer.exe",
}


def stamp():
    """Visszaadja a pontos időbélyeget ISO formátumban."""
    return datetime.now().astimezone().isoformat(timespec="seconds")


def append_log(text):
    """Szöveg hozzáfűzése a naplófájlhoz."""
    try:
        with LOG_PATH.open("a", encoding="utf-8") as file:
            file.write(text + "\n")
    except OSError as e:
        print(f"Hiba a napló írásakor: {e}", file=sys.stderr)


def collect_processes():
    """Összegyűjti az összes futó folyamat adatait RAM-használat szerint rendezve."""
    rows = []
    for process in psutil.process_iter():
        try:
            with process.oneshot():
                memory = process.memory_info()
                name = process.name()
                created = process.create_time()
                try:
                    executable = process.exe() or "(nem elérhető)"
                except (psutil.AccessDenied, psutil.Error, PermissionError, OSError):
                    executable = "(nincs hozzáférés - rendszer/más felhasználó)"
                rows.append({
                    "pid": process.pid,
                    "name": name,
                    "rss": memory.rss,
                    "created": created,
                    "exe": executable,
                })
        except (psutil.Error, OSError):
            continue
    return sorted(rows, key=lambda row: row["rss"], reverse=True)


def write_report(reset=False):
    """
    Összeállítja a memóriajelentést.
    Ha reset=True, felülírja (törli és újraírja) a naplófájlt.
    Ha reset=False, hozzáfűzi a friss állapotot.
    """
    memory = psutil.virtual_memory()
    rows = collect_processes()
    
    total_gib = memory.total / (1024**3)
    avail_gib = memory.available / (1024**3)
    used_gib = (memory.total - memory.available) / (1024**3)
    
    lines = [
        "=" * 100,
        f"MEMÓRIA JELENTÉS: {stamp()} {'[NAPLÓ ÚJRAKEZDVE / FRISSÍTVE]' if reset else '[IDŐSZAKOS FRISSÍTÉS]'}",
        f"Összes fizikai RAM: {total_gib:.2f} GiB",
        f"Használt RAM:        {used_gib:.2f} GiB ({memory.percent:.1f}%)",
        f"Elérhető / Szabad:  {avail_gib:.2f} GiB",
        "=" * 100,
        "Megjegyzés: Az RSS (Working Set) a folyamat fizikai RAM-használatát mutatja.",
        "A Windows megosztott memórialapokat is használhat a folyamatok között.",
        "-" * 100,
        "",
    ]

    for rank, row in enumerate(rows, 1):
        name_lower = row["name"].lower()
        description = DESCRIPTIONS.get(
            name_lower,
            "Alkalmazás vagy háttérfolyamat (lásd az elérési utat)",
        )
        rss_mib = row["rss"] / (1024**2)
        rss_percent = (row["rss"] / memory.total) * 100
        
        # Csak a 10 MB feletti folyamatokat részletezzük a naplóban, hogy átlátható maradjon
        if rss_mib < 10.0 and rank > 30:
            continue

        lines.extend([
            f'#{rank:02d} | {row["name"]} (PID: {row["pid"]})',
            f'     Fogyasztás: {rss_mib:.1f} MiB ({rss_percent:.2f}% a teljes RAM-ból)',
            f'     Mi ez:      {description}',
            f'     Útvonal:    {row["exe"]}',
            "",
        ])

    lines.append("")
    mode = "w" if reset else "a"
    with LOG_PATH.open(mode, encoding="utf-8") as file:
        file.write("\n".join(lines) + "\n")

    action_str = "Napló törölve és újrakezdve (10 perc)" if reset else "Jelentés hozzáfűzve (5 perc)"
    print(
        f"[{stamp()}] RAM terhelés: {memory.percent:.1f}% | "
        f"Szabad: {avail_gib:.2f} GiB | {action_str}",
        flush=True,
    )


def windows_api():
    """Windows Kernel32, PSAPI és User32 API függvények betöltése a tisztításhoz."""
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    psapi = ctypes.WinDLL("psapi", use_last_error=True)
    user = ctypes.WinDLL("user32", use_last_error=True)

    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel.CloseHandle.restype = wintypes.BOOL
    
    psapi.EmptyWorkingSet.argtypes = [wintypes.HANDLE]
    psapi.EmptyWorkingSet.restype = wintypes.BOOL
    
    user.GetForegroundWindow.argtypes = []
    user.GetForegroundWindow.restype = wintypes.HWND
    user.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user.GetWindowThreadProcessId.restype = wintypes.DWORD
    
    return kernel, psapi, user


def clean_selected(allowed, auto_all, threshold, cooldowns, api, owner):
    """
    Biztonságosan kiüríti a kiválasztott vagy magas fogyasztású nem-aktív folyamatok munkakészletét (EmptyWorkingSet),
    ha a teljes rendszer memóriahasználata meghaladja a megadott küszöbértéket (pl. 80%).
    """
    current_ram_percent = psutil.virtual_memory().percent
    if current_ram_percent < threshold:
        return

    kernel, psapi, user = api
    foreground_pid = wintypes.DWORD()
    user.GetWindowThreadProcessId(user.GetForegroundWindow(), ctypes.byref(foreground_pid))

    now = time.monotonic()
    rows = collect_processes()
    
    # Inaktív cooldown kulcsok takarítása
    live_keys = {(r["pid"], r["created"]) for r in rows}
    for key in list(cooldowns):
        if key not in live_keys:
            del cooldowns[key]

    for row in rows:
        if psutil.virtual_memory().percent < threshold:
            break

        name_lower = row["name"].lower()
        key = (row["pid"], row["created"])

        # Szűrési szabályok:
        # 1. Ne legyen védett rendszerelem
        if name_lower in PROTECTED:
            continue

        # 2. Ha meg van adva konkrét lista (--trim), és a folyamat nincs benne, és nincs auto_all
        if not auto_all and allowed and name_lower not in allowed:
            continue
        if not auto_all and not allowed:
            continue

        # 3. Ne tisztítsuk az épp használt előtéri ablakot, a saját scriptünket vagy a System-et
        if row["pid"] in (0, 4, os.getpid(), foreground_pid.value):
            continue

        # 4. Ne tisztítsunk kis memóriájú folyamatot (min 250 MB)
        if row["rss"] < MIN_TRIM_MB * 1024**2:
            continue

        # 5. Cooldown ellenőrzés (10 perc)
        if now - cooldowns.get(key, float("-inf")) < TRIM_COOLDOWN:
            continue

        handle = None
        try:
            process = psutil.Process(row["pid"])
            if process.create_time() != row["created"]:
                continue
            
            # Csak saját felhasználói folyamatokat tisztítunk emelt jogok nélkül
            try:
                proc_user = process.username()
                if proc_user and proc_user != owner and not ctypes.windll.shell32.IsUserAnAdmin():
                    continue
            except (psutil.AccessDenied, psutil.Error):
                continue

            # Regisztráljuk a próbálkozást
            cooldowns[key] = now

            # PROCESS_SET_QUOTA (0x0100) | PROCESS_QUERY_LIMITED_INFORMATION (0x1000)
            handle = kernel.OpenProcess(0x0100 | 0x1000, False, row["pid"])
            if not handle:
                continue

            if not process.is_running():
                continue

            before_rss = process.memory_info().rss
            success = psapi.EmptyWorkingSet(handle)
            if success:
                after_rss = process.memory_info().rss
                freed_mb = (before_rss - after_rss) / (1024**2)
                if freed_mb > 5:
                    msg = (
                        f"[{stamp()}] Munkakészlet ürítve: {row['name']} (PID: {row['pid']}) | "
                        f"{before_rss / 1024**2:.1f} MiB -> {after_rss / 1024**2:.1f} MiB "
                        f"(Felszabadítva: {freed_mb:.1f} MiB)"
                    )
                    append_log(msg)
                    print(msg, flush=True)
        except (psutil.Error, OSError):
            continue
        finally:
            if handle:
                kernel.CloseHandle(handle)


def main():
    if os.name != "nt":
        raise SystemExit("Ez a program kifejezetten Windows 10/11 rendszerekre készült.")

    parser = argparse.ArgumentParser(
        description="Windows RAM megfigyelő és intelligens memóriatisztító."
    )
    parser.add_argument(
        "--trim", nargs="+", default=[], metavar="PROGRAM.EXE",
        help="Célzott alkalmazások megadása tisztításhoz (pl. --trim discord.exe steamwebhelper.exe chrome.exe)."
    )
    parser.add_argument(
        "--auto-trim", action="store_true",
        help="Automatikus tisztítás minden 250 MB feletti háttér-alkalmazásra magas RAM-használat esetén."
    )
    parser.add_argument(
        "--threshold", type=float, default=80.0,
        help="Memória küszöbérték (százalékban), ami felett elindul a tisztítás (alapértelmezett: 80%%)."
    )
    args = parser.parse_args()

    if not 1 <= args.threshold <= 100:
        parser.error("A küszöbérték 1 és 100 közötti százalék legyen.")

    allowed = {name.lower() for name in args.trim}
    auto_all = args.auto_trim
    owner = psutil.Process().username()
    api = windows_api()
    cooldowns = {}

    print("=" * 70)
    print("      WISE MEMORY WIPER / RAM MONITOR & OPTIMIZER (WIN10)")
    print("=" * 70)
    print(f"Naplófájl útvonala: {LOG_PATH}")
    print("Naplózás: 5 percenként hozzáfűzés | 10 percenként TELJES TÖRLESE ÉS ÚJRAÍRÁSA")
    print(f"Memóriatisztítás küszöbértéke: {args.threshold:.1f}% RAM terhelés felett")
    
    if auto_all:
        print("Tisztítás módja: AUTOMATIKUS (Minden háttérben lévő >250MB fogyasztású app)")
    elif allowed:
        print(f"Tisztítás módja: CÉLZOTT ({', '.join(sorted(allowed))})")
    else:
        print("Tisztítás módja: KIKAPCSOLVA (Csak megfigyelés és naplózás)")
        print("Tip: Használd a '--auto-trim' vagy '--trim discord.exe chrome.exe' kapcsolót a tisztításhoz.")
    
    print("Leállítás: Ctrl+C")
    print("=" * 70)

    # 1. Indítási jelentés készítése és a napló törlése/újrakezdése
    write_report(reset=True)
    
    now = time.monotonic()
    next_log = now + LOG_INTERVAL
    next_reset = now + RESET_INTERVAL

    try:
        while True:
            now = time.monotonic()
            
            # 10 perces törlés / felülírás ciklus
            if now >= next_reset:
                write_report(reset=True)
                next_reset = now + RESET_INTERVAL
                next_log = now + LOG_INTERVAL
            # 5 perces naplózási ciklus
            elif now >= next_log:
                write_report(reset=False)
                next_log = now + LOG_INTERVAL

            # 15 másodpercenkénti ellenőrzés és esetleges memóriatisztítás
            clean_selected(
                allowed, auto_all, args.threshold, cooldowns, api, owner
            )
            time.sleep(CHECK_INTERVAL)
            
    except KeyboardInterrupt:
        print("\n[LEÁLLÍTVA] A memóriafigyelő leállt.")
    except OSError as error:
        print(f"\n[HIBA] Fájl- vagy rendszerhiba: {error}")


if __name__ == "__main__":
    main()
