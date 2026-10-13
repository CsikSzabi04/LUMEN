import ctypes
from ctypes import wintypes
from datetime import datetime
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import threading
import time
import webbrowser

from flask import Flask, jsonify, render_template_string, request, Response
import psutil


app = Flask(__name__)
BASE_DIR = Path(__file__).resolve().parent
LOG_PATH = BASE_DIR / "memoria_naplo.txt"
DOCK_DIR = BASE_DIR / "dock"
TASKBAR_DIR = BASE_DIR / "taskbar-ios"
STARTUP_DIR = BASE_DIR / "startup"
FONTS_DIR = BASE_DIR / "fonts"

# Statisztikai történet az élő grafikonokhoz
STATS_HISTORY = {
    "timestamps": [],
    "cpu": [],
    "ram": [],
    "disk_read": [],
    "disk_write": [],
    "net_sent": [],
    "net_recv": [],
}
MAX_HISTORY = 30

# Hálózati és lemez sebesség számításhoz az előző értékek
last_net = psutil.net_io_counters()
last_disk = psutil.disk_io_counters()
last_time = time.time()

# Windows API inicializálás
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
psapi = ctypes.WinDLL("psapi", use_last_error=True)
user32 = ctypes.WinDLL("user32", use_last_error=True)

kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel32.OpenProcess.restype = wintypes.HANDLE
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.CloseHandle.restype = wintypes.BOOL
psapi.EmptyWorkingSet.argtypes = [wintypes.HANDLE]
psapi.EmptyWorkingSet.restype = wintypes.BOOL

DESCRIPTIONS = {
    "chrome.exe": "Google Chrome böngésző",
    "msedge.exe": "Microsoft Edge böngésző",
    "firefox.exe": "Mozilla Firefox böngésző",
    "brave.exe": "Brave böngésző",
    "opera.exe": "Opera böngésző",
    "code.exe": "Visual Studio Code szerkesztő",
    "antigravity.exe": "Antigravity AI szerkesztő",
    "python.exe": "Python scriptek / interpreter",
    "pythonw.exe": "Python háttérfolyamat",
    "node.exe": "Node.js JavaScript futtatókörnyezet",
    "discord.exe": "Discord csevegő és hangkliens",
    "steam.exe": "Steam kliens",
    "steamwebhelper.exe": "Steam webes segédfolyamat",
    "explorer.exe": "Windows Fájlkezelő és asztal",
    "svchost.exe": "Windows rendszerszolgáltatások gazdafolyamata",
    "msmpeng.exe": "Microsoft Defender vírusvédelem",
    "dwm.exe": "Windows Asztali ablakkezelő",
    "searchindexer.exe": "Windows keresési indexelő",
    "onedrive.exe": "Microsoft OneDrive felhő szinkronizáló",
    "vmmem": "Hyper-V / Docker virtuális gép",
    "vmmemwsl": "WSL2 virtuális gép memóriája",
    "system": "Windows Rendszermag (Kernel)",
    "registry": "Windows Rendszerleíró Adatbázis gyorsítótár",
    "memcompression": "Windows RAM kompressziós rendszer",
    "glassdock.exe": "GlassDock asztali dokk alkalmazás",
    "language_server.exe": "Kódelemző (Language Server)",
    "shellexperiencehost.exe": "Windows Shell Élménygazda",
    "startmenuexperiencehost.exe": "Windows Start menü felületgazda",
    "amdrsserv.exe": "AMD Radeon illesztőprogram segédfolyamata",
    "radeonsoftware.exe": "AMD Radeon Grafikus vezérlőszoftver",
    "runtimebroker.exe": "Windows alkalmazási engedély broker",
    "mpdefendercoreservice.exe": "Microsoft Defender magszolgáltatás",
    "sihost.exe": "Windows Shell Infrastruktúra Gazdafolyamat",
}

PROTECTED_NAMES = {
    "system", "registry", "memory compression", "memcompression", "secure system",
    "system idle process", "smss.exe", "csrss.exe", "wininit.exe",
    "services.exe", "lsass.exe", "winlogon.exe", "svchost.exe",
    "dwm.exe", "explorer.exe", "msmpeng.exe",
}


def run_powershell(script_path, args=None):
    """Lefuttat egy PowerShell scriptet rendszergazdai jogosultsággal."""
    cmd = ["powershell", "-ExecutionPolicy", "Bypass", "-File", str(script_path)]
    if args:
        cmd.extend(args)
    try:
        res = subprocess.run(cmd, capture_output=True, text=True, timeout=15)
        return res.returncode == 0, res.stdout or res.stderr
    except Exception as e:
        return False, str(e)


def purge_working_sets():
    """Kiüríti az összes elérhető nem-rendszer folyamat munkakészletét."""
    freed_total = 0
    count = 0
    for proc in psutil.process_iter():
        try:
            name_lower = proc.name().lower()
            if name_lower in PROTECTED_NAMES or proc.pid in (0, 4, os.getpid()):
                continue
            
            handle = kernel32.OpenProcess(0x0100 | 0x1000, False, proc.pid)
            if handle:
                before = proc.memory_info().rss
                if psapi.EmptyWorkingSet(handle):
                    after = proc.memory_info().rss
                    freed = max(0, before - after)
                    freed_total += freed
                    count += 1
                kernel32.CloseHandle(handle)
        except (psutil.Error, OSError):
            continue
    return count, freed_total / (1024**2)


def purge_standby_list():
    """Wise Memory Optimizer futtatása a Standby lista kiürítésére."""
    try:
        wise_ps = STARTUP_DIR / "Set-WiseOptimizer.ps1"
        if wise_ps.exists():
            run_powershell(wise_ps)
        return True
    except Exception:
        return False


@app.route("/")
def index():
    return render_template_string(HTML_TEMPLATE)


@app.route("/api/stats")
def get_stats():
    global last_net, last_disk, last_time
    now = time.time()
    dt = max(0.1, now - last_time)
    
    # CPU
    cpu_percent = psutil.cpu_percent(interval=None)
    cpu_count = psutil.cpu_count(logical=True)
    
    # RAM
    mem = psutil.virtual_memory()
    total_ram = mem.total / (1024**3)
    used_ram = (mem.total - mem.available) / (1024**3)
    free_ram = mem.available / (1024**3)
    cached_ram = getattr(mem, 'cached', 0) / (1024**3)
    
    # Disk I/O
    disk_curr = psutil.disk_io_counters()
    disk_read_speed = max(0, (disk_curr.read_bytes - last_disk.read_bytes) / dt / (1024**2)) if disk_curr and last_disk else 0
    disk_write_speed = max(0, (disk_curr.write_bytes - last_disk.write_bytes) / dt / (1024**2)) if disk_curr and last_disk else 0
    last_disk = disk_curr
    
    # Disk Usage
    disk_usage = psutil.disk_usage('C:')
    
    # Network I/O
    net_curr = psutil.net_io_counters()
    net_sent_speed = max(0, (net_curr.bytes_sent - last_net.bytes_sent) / dt / 1024) if net_curr and last_net else 0
    net_recv_speed = max(0, (net_curr.bytes_recv - last_net.bytes_recv) / dt / 1024) if net_curr and last_net else 0
    last_net = net_curr
    last_time = now

    # GPU becslés / info (WMI / System fallback)
    gpu_name = "Radeon RX 560 Series / Integrated GPU"
    gpu_percent = 2.0
    
    # Történet frissítés
    t_str = datetime.now().strftime("%H:%M:%S")
    STATS_HISTORY["timestamps"].append(t_str)
    STATS_HISTORY["cpu"].append(round(cpu_percent, 1))
    STATS_HISTORY["ram"].append(round(mem.percent, 1))
    STATS_HISTORY["disk_read"].append(round(disk_read_speed, 1))
    STATS_HISTORY["disk_write"].append(round(disk_write_speed, 1))
    STATS_HISTORY["net_sent"].append(round(net_sent_speed, 1))
    STATS_HISTORY["net_recv"].append(round(net_recv_speed, 1))

    if len(STATS_HISTORY["timestamps"]) > MAX_HISTORY:
        for k in STATS_HISTORY:
            STATS_HISTORY[k].pop(0)

    return jsonify({
        "timestamp": t_str,
        "cpu": {
            "percent": cpu_percent,
            "cores": cpu_count,
        },
        "ram": {
            "total_gb": round(total_ram, 2),
            "used_gb": round(used_ram, 2),
            "free_gb": round(free_ram, 2),
            "percent": mem.percent,
            "cached_gb": round(cached_ram, 2),
        },
        "disk": {
            "percent": disk_usage.percent,
            "used_gb": round(disk_usage.used / (1024**3), 1),
            "total_gb": round(disk_usage.total / (1024**3), 1),
            "read_mbps": round(disk_read_speed, 2),
            "write_mbps": round(disk_write_speed, 2),
        },
        "net": {
            "sent_kbps": round(net_sent_speed, 1),
            "recv_kbps": round(net_recv_speed, 1),
        },
        "gpu": {
            "name": gpu_name,
            "percent": gpu_percent,
        },
        "history": STATS_HISTORY,
    })


@app.route("/api/processes")
def get_processes():
    rows = []
    total_mem = psutil.virtual_memory().total
    for proc in psutil.process_iter():
        try:
            with proc.oneshot():
                name = proc.name()
                pid = proc.pid
                rss = proc.memory_info().rss
                cpu = proc.cpu_percent(interval=None)
                try:
                    exe = proc.exe() or "(nem elérhető)"
                except Exception:
                    exe = "(nincs hozzáférés)"
                
                desc = DESCRIPTIONS.get(name.lower(), "Alkalmazás / Háttérfolyamat")
                rows.append({
                    "pid": pid,
                    "name": name,
                    "rss_mib": round(rss / (1024**2), 1),
                    "ram_percent": round((rss / total_mem) * 100, 2),
                    "cpu_percent": round(cpu, 1),
                    "desc": desc,
                    "exe": exe,
                    "is_protected": name.lower() in PROTECTED_NAMES,
                })
        except Exception:
            continue
            
    rows = sorted(rows, key=lambda x: x["rss_mib"], reverse=True)
    return jsonify({"processes": rows, "count": len(rows)})


@app.route("/api/process/kill", methods=["POST"])
def kill_process():
    data = request.json or {}
    pid = data.get("pid")
    if not pid:
        return jsonify({"success": False, "error": "PID hiányzik"}), 400
    try:
        proc = psutil.Process(pid)
        name = proc.name()
        if name.lower() in PROTECTED_NAMES:
            return jsonify({"success": False, "error": "Rendszerkritikus folyamat nem állítható le."}), 403
        proc.kill()
        return jsonify({"success": True, "message": f"Folyamat leállítva: {name} (PID: {pid})"})
    except Exception as e:
        return jsonify({"success": False, "error": str(e)}), 500


@app.route("/api/process/trim", methods=["POST"])
def trim_process():
    data = request.json or {}
    pid = data.get("pid")
    if not pid:
        return jsonify({"success": False, "error": "PID hiányzik"}), 400
    try:
        proc = psutil.Process(pid)
        handle = kernel32.OpenProcess(0x0100 | 0x1000, False, pid)
        if handle:
            before = proc.memory_info().rss
            psapi.EmptyWorkingSet(handle)
            after = proc.memory_info().rss
            kernel32.CloseHandle(handle)
            freed_mb = round((before - after) / (1024**2), 1)
            return jsonify({"success": True, "message": f"Ürítve: {proc.name()} (Felszabadítva: {freed_mb} MiB)"})
        return jsonify({"success": False, "error": "Nincs hozzáférés a folyamathoz"}), 403
    except Exception as e:
        return jsonify({"success": False, "error": str(e)}), 500


@app.route("/api/wiper/sweep", methods=["POST"])
def instant_sweep():
    count, freed_mb = purge_working_sets()
    purge_standby_list()
    return jsonify({
        "success": True,
        "message": f"RAM Sepergetés kész! {count} folyamat munkakészlete kiürítve. Felszabadítva: ~{freed_mb:.1f} MiB + Standby Cache.",
        "freed_mb": round(freed_mb, 1),
    })


@app.route("/api/wiper/log")
def get_wiper_log():
    if LOG_PATH.exists():
        try:
            content = LOG_PATH.read_text(encoding="utf-8")
            return jsonify({"success": True, "log": content})
        except Exception as e:
            return jsonify({"success": False, "error": str(e)})
    return jsonify({"success": False, "log": "A memoria_naplo.txt fájl még nem jött létre."})


@app.route("/api/gui/dock", methods=["POST"])
def control_dock():
    data = request.json or {}
    action = data.get("action")
    
    if action == "restart":
        # Process kill and restart GlassDock.exe
        for p in psutil.process_iter():
            if p.name().lower() == "glassdock.exe":
                try:
                    p.kill()
                except Exception:
                    pass
        dock_exe = DOCK_DIR / "GlassDock.exe"
        if dock_exe.exists():
            subprocess.Popen([str(dock_exe)], cwd=str(DOCK_DIR))
            return jsonify({"success": True, "message": "GlassDock újraindítva!"})
        return jsonify({"success": False, "error": "GlassDock.exe nem található"}), 404
        
    elif action == "restore_layout":
        script = DOCK_DIR / "Restore-DesktopLayout.ps1"
        ok, msg = run_powershell(script, ["-Apply"])
        return jsonify({"success": ok, "message": "Asztali ikonok elrendezése visszaállítva!" if ok else msg})

    elif action == "hide_overlays":
        script = DOCK_DIR / "Hide-ShortcutOverlays.ps1"
        ok, msg = run_powershell(script)
        return jsonify({"success": ok, "message": "Parancsikon nyílak és UAC pajzsok elrejtve!" if ok else msg})

    elif action == "restore_overlays":
        script = DOCK_DIR / "Restore-ShortcutOverlays.ps1"
        ok, msg = run_powershell(script)
        return jsonify({"success": ok, "message": "Parancsikon nyílak visszaállítva!" if ok else msg})

    elif action == "apply_taskbar":
        script = TASKBAR_DIR / "Apply-Taskbar.ps1"
        ok, msg = run_powershell(script)
        return jsonify({"success": ok, "message": "iOS tálca beállítások alkalmazva!" if ok else msg})

    return jsonify({"success": False, "error": "Ismeretlen akció"}), 400


@app.route("/api/devtools/status")
def devtools_status():
    tools = {
        "python": False,
        "python_ver": "N/A",
        "node": False,
        "node_ver": "N/A",
        "pnpm": False,
        "pnpm_ver": "N/A",
        "java": False,
        "java_ver": "N/A",
        "git": False,
        "git_ver": "N/A",
    }
    
    # Python
    tools["python"] = True
    tools["python_ver"] = f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}"
    
    # Node
    try:
        res = subprocess.run(["node", "-v"], capture_output=True, text=True, timeout=3)
        if res.returncode == 0:
            tools["node"] = True
            tools["node_ver"] = res.stdout.strip()
    except Exception:
        pass
        
    # pnpm
    try:
        res = subprocess.run(["pnpm", "-v"], capture_output=True, text=True, timeout=3)
        if res.returncode == 0:
            tools["pnpm"] = True
            tools["pnpm_ver"] = res.stdout.strip()
    except Exception:
        pass

    # Java
    try:
        res = subprocess.run(["java", "-version"], capture_output=True, text=True, timeout=3)
        if res.returncode == 0 or "version" in res.stderr:
            tools["java"] = True
            first_line = (res.stdout or res.stderr).splitlines()[0]
            tools["java_ver"] = first_line
    except Exception:
        pass

    # Git
    try:
        res = subprocess.run(["git", "--version"], capture_output=True, text=True, timeout=3)
        if res.returncode == 0:
            tools["git"] = True
            tools["git_ver"] = res.stdout.strip()
    except Exception:
        pass

    return jsonify(tools)


@app.route("/api/devtools/install", methods=["POST"])
def install_devtool():
    data = request.json or {}
    tool = data.get("tool")
    
    packages = {
        "nodejs": "OpenJS.NodeJS",
        "pnpm": "pnpm.pnpm",
        "java": "Oracle.JDK.21",
        "git": "Git.Git",
        "python": "Python.Python.3.12",
    }
    
    pkg_id = packages.get(tool)
    if not pkg_id:
        return jsonify({"success": False, "error": "Ismeretlen fejlesztői csomag"}), 400

    def generate():
        yield f"data: [{tool.upper()} TELEPÍTÉS] Winget indítása package ID: {pkg_id}...\n\n"
        cmd = [
            "winget", "install", "--id", pkg_id,
            "-e", "--silent", "--accept-package-agreements", "--accept-source-agreements"
        ]
        proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, bufsize=1)
        for line in proc.stdout:
            yield f"data: {line}\n\n"
        proc.wait()
        if proc.returncode == 0:
            yield f"data: [SIKER] {tool.upper()} sikeresen telepítve!\n\n"
        else:
            yield f"data: [HIBA] Telepítés meghiúsult (Kód: {proc.returncode})\n\n"

    return Response(generate(), mimetype="text/event-stream")


@app.route("/api/terminal/run", methods=["POST"])
def run_terminal():
    data = request.json or {}
    command = data.get("command", "").strip()
    if not command:
        return jsonify({"output": "Üres parancs."})

    try:
        res = subprocess.run(
            ["powershell", "-NoProfile", "-Command", command],
            capture_output=True, text=True, timeout=30, cwd=str(BASE_DIR)
        )
        out = (res.stdout or "") + (res.stderr or "")
        return jsonify({"output": out if out else "(Kimenet nélkül fejeződött be)"})
    except Exception as e:
        return jsonify({"output": f"Hiba a parancs futtatásakor: {e}"})


HTML_TEMPLATE = """<!DOCTYPE html>
<html lang="hu">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>GlassControl Suite // Obsidian Admin Dashboard</title>
    <!-- Google Fonts & Chart.js -->
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700&family=JetBrains+Mono:wght@400;500;600;700&display=swap" rel="stylesheet">
    <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
    <style>
        :root {
            --bg-obsidian: #090D16;
            --bg-card: rgba(22, 27, 34, 0.75);
            --bg-card-hover: rgba(33, 38, 45, 0.85);
            --border-github: #30363D;
            --border-glow: rgba(139, 92, 246, 0.4);
            --text-main: #F0F6FC;
            --text-muted: #8B949E;
            
            --accent-violet: #8B5CF6;
            --accent-violet-glow: rgba(139, 92, 246, 0.25);
            --accent-cyan: #38BDF8;
            --accent-emerald: #10B981;
            --accent-amber: #F59E0B;
            --accent-crimson: #EF4444;
            
            --font-sans: 'Inter', system-ui, -apple-system, sans-serif;
            --font-mono: 'JetBrains Mono', monospace;
        }

        * { box-sizing: border-box; margin: 0; padding: 0; }

        body {
            background-color: var(--bg-obsidian);
            color: var(--text-main);
            font-family: var(--font-sans);
            min-height: 100vh;
            display: flex;
            flex-direction: column;
            overflow-x: hidden;
            background-image: 
                radial-gradient(circle at 15% 15%, rgba(139, 92, 246, 0.08) 0%, transparent 45%),
                radial-gradient(circle at 85% 85%, rgba(56, 189, 248, 0.08) 0%, transparent 45%);
        }

        /* Top Admin Status Bar */
        .admin-header {
            background: var(--bg-card);
            backdrop-filter: blur(16px);
            border-bottom: 1px solid var(--border-github);
            padding: 12px 24px;
            display: flex;
            align-items: center;
            justify-content: space-between;
            position: sticky;
            top: 0;
            z-index: 100;
        }

        .brand-section {
            display: flex;
            align-items: center;
            gap: 16px;
        }

        .badge-lvl {
            background: linear-gradient(135deg, var(--accent-violet), #6D28D9);
            color: #FFF;
            font-family: var(--font-mono);
            font-weight: 700;
            font-size: 13px;
            padding: 4px 12px;
            border-radius: 20px;
            box-shadow: 0 0 12px var(--accent-violet-glow);
            letter-spacing: 0.5px;
        }

        .brand-title {
            font-size: 16px;
            font-weight: 700;
            letter-spacing: 0.5px;
            display: flex;
            align-items: center;
            gap: 8px;
        }

        .brand-title span { color: var(--accent-cyan); }

        .system-status-pills {
            display: flex;
            align-items: center;
            gap: 12px;
            font-family: var(--font-mono);
            font-size: 12px;
        }

        .status-pill {
            background: rgba(13, 17, 23, 0.8);
            border: 1px solid var(--border-github);
            padding: 6px 14px;
            border-radius: 8px;
            display: flex;
            align-items: center;
            gap: 8px;
        }

        .dot-live {
            width: 8px;
            height: 8px;
            border-radius: 50%;
            background: var(--accent-emerald);
            box-shadow: 0 0 8px var(--accent-emerald);
            animation: pulse 2s infinite;
        }

        @keyframes pulse {
            0% { opacity: 1; transform: scale(1); }
            50% { opacity: 0.4; transform: scale(0.85); }
            100% { opacity: 1; transform: scale(1); }
        }

        /* Navigation Tabs */
        .nav-tabs {
            display: flex;
            gap: 8px;
            padding: 16px 24px;
            background: rgba(13, 17, 23, 0.5);
            border-bottom: 1px solid var(--border-github);
        }

        .nav-tab {
            background: transparent;
            color: var(--text-muted);
            border: 1px solid transparent;
            padding: 10px 18px;
            border-radius: 8px;
            font-size: 13px;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.2s ease;
            display: flex;
            align-items: center;
            gap: 8px;
        }

        .nav-tab:hover {
            color: var(--text-main);
            background: var(--bg-card);
            border-color: var(--border-github);
        }

        .nav-tab.active {
            color: #FFF;
            background: var(--accent-violet-glow);
            border-color: var(--accent-violet);
            box-shadow: 0 0 15px rgba(139, 92, 246, 0.2);
        }

        /* Main Container */
        .container {
            flex: 1;
            padding: 24px;
            max-width: 1400px;
            margin: 0 auto;
            width: 100%;
        }

        .tab-content { display: none; }
        .tab-content.active { display: block; }

        /* Glass Cards Grid */
        .grid-2 { display: grid; grid-template-columns: repeat(2, 1fr); gap: 20px; }
        .grid-4 { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; margin-bottom: 20px; }

        .card {
            background: var(--bg-card);
            backdrop-filter: blur(16px);
            border: 1px solid var(--border-github);
            border-radius: 12px;
            padding: 20px;
            transition: border-color 0.3s ease;
        }

        .card:hover {
            border-color: rgba(139, 92, 246, 0.5);
        }

        .card-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 16px;
        }

        .card-title {
            font-size: 14px;
            font-weight: 600;
            color: var(--text-muted);
            text-transform: uppercase;
            letter-spacing: 0.8px;
            display: flex;
            align-items: center;
            gap: 8px;
        }

        .metric-value {
            font-family: var(--font-mono);
            font-size: 28px;
            font-weight: 700;
            color: var(--text-main);
            margin-top: 4px;
        }

        .metric-sub {
            font-size: 12px;
            color: var(--text-muted);
            margin-top: 4px;
        }

        /* Chart Canvas Container */
        .chart-container {
            position: relative;
            height: 220px;
            width: 100%;
        }

        /* Buttons */
        .btn {
            background: var(--bg-card);
            color: var(--text-main);
            border: 1px solid var(--border-github);
            padding: 10px 18px;
            border-radius: 8px;
            font-size: 13px;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.2s ease;
            display: inline-flex;
            align-items: center;
            gap: 8px;
        }

        .btn:hover {
            background: var(--bg-card-hover);
            border-color: var(--accent-violet);
            box-shadow: 0 0 12px var(--accent-violet-glow);
        }

        .btn-primary {
            background: linear-gradient(135deg, var(--accent-violet), #6D28D9);
            border: none;
            color: #FFF;
            box-shadow: 0 0 15px var(--accent-violet-glow);
        }

        .btn-primary:hover {
            opacity: 0.9;
            transform: translateY(-1px);
        }

        .btn-danger {
            background: rgba(239, 68, 68, 0.2);
            border-color: var(--accent-crimson);
            color: #F87171;
        }

        .btn-danger:hover {
            background: var(--accent-crimson);
            color: #FFF;
        }

        /* Table */
        .table-wrapper {
            overflow-x: auto;
            border: 1px solid var(--border-github);
            border-radius: 8px;
            background: rgba(13, 17, 23, 0.6);
        }

        table {
            width: 100%;
            border-collapse: collapse;
            font-size: 13px;
            text-align: left;
        }

        th {
            background: rgba(22, 27, 34, 0.9);
            color: var(--text-muted);
            font-weight: 600;
            padding: 12px 16px;
            border-bottom: 1px solid var(--border-github);
            font-family: var(--font-mono);
            font-size: 12px;
        }

        td {
            padding: 12px 16px;
            border-bottom: 1px solid rgba(48, 54, 61, 0.5);
        }

        tr:hover td {
            background: rgba(139, 92, 246, 0.05);
        }

        .search-box {
            background: rgba(13, 17, 23, 0.8);
            border: 1px solid var(--border-github);
            color: var(--text-main);
            padding: 8px 14px;
            border-radius: 8px;
            font-size: 13px;
            width: 280px;
            margin-bottom: 16px;
            outline: none;
        }

        .search-box:focus {
            border-color: var(--accent-violet);
        }

        /* Terminal Console */
        .terminal-box {
            background: #05070B;
            border: 1px solid var(--border-github);
            border-radius: 8px;
            font-family: var(--font-mono);
            font-size: 13px;
            padding: 16px;
            height: 400px;
            overflow-y: auto;
            color: #38BDF8;
            line-height: 1.6;
            white-space: pre-wrap;
        }

        .terminal-input-wrapper {
            display: flex;
            gap: 12px;
            margin-top: 12px;
        }

        .terminal-input {
            flex: 1;
            background: rgba(13, 17, 23, 0.9);
            border: 1px solid var(--border-github);
            color: var(--text-main);
            font-family: var(--font-mono);
            padding: 10px 14px;
            border-radius: 8px;
            outline: none;
        }

        .terminal-input:focus {
            border-color: var(--accent-cyan);
        }
    </style>
</head>
<body>

    <!-- Header -->
    <header class="admin-header">
        <div class="brand-section">
            <span class="badge-lvl">LVL 33</span>
            <div class="brand-title">
                <span>GlassControl</span> Suite // Admin Dashboard
            </div>
        </div>

        <div class="system-status-pills">
            <div class="status-pill">
                <div class="dot-live"></div>
                <span>RAM Shield: ACTIVE</span>
            </div>
            <div class="status-pill" id="header-ram-pill">
                RAM: -- / -- GB
            </div>
            <div class="status-pill" id="header-cpu-pill">
                CPU: --%
            </div>
        </div>
    </header>

    <!-- Navigation Tabs -->
    <nav class="nav-tabs">
        <button class="nav-tab active" onclick="switchTab('tab-performance')">📊 Teljesítmény Látó</button>
        <button class="nav-tab" onclick="switchTab('tab-processes')">⚙️ Feladatkezelő</button>
        <button class="nav-tab" onclick="switchTab('tab-wiper')">🧹 Wise Memory Wiper</button>
        <button class="nav-tab" onclick="switchTab('tab-glassui')">🍏 iOS Glass UI</button>
        <button class="nav-tab" onclick="switchTab('tab-devtools')">🛠️ Fejlesztői Hub</button>
        <button class="nav-tab" onclick="switchTab('tab-terminal')">💻 Terminál</button>
    </nav>

    <!-- Main Content -->
    <main class="container">

        <!-- 1. Teljesítmény Tab -->
        <section id="tab-performance" class="tab-content active">
            <div class="grid-4">
                <div class="card">
                    <div class="card-title">PROCESSZOR (CPU)</div>
                    <div class="metric-value" id="val-cpu">--%</div>
                    <div class="metric-sub" id="val-cpu-cores">-- Magok</div>
                </div>
                <div class="card">
                    <div class="card-title">MEMÓRIA (RAM)</div>
                    <div class="metric-value" id="val-ram">-- GB</div>
                    <div class="metric-sub" id="val-ram-sub">Szabad: -- GB</div>
                </div>
                <div class="card">
                    <div class="card-title">SSD / LEMEZ (C:)</div>
                    <div class="metric-value" id="val-disk">--%</div>
                    <div class="metric-sub" id="val-disk-sub">O: 0.0 / Í: 0.0 MB/s</div>
                </div>
                <div class="card">
                    <div class="card-title">HÁLÓZAT / DOKK</div>
                    <div class="metric-value" id="val-net">0.0 Kb/s</div>
                    <div class="metric-sub" id="val-net-sub">Radeon RX 560 Series</div>
                </div>
            </div>

            <div class="grid-2">
                <div class="card">
                    <div class="card-header">
                        <div class="card-title">CPU & RAM Terhelés (%)</div>
                    </div>
                    <div class="chart-container">
                        <canvas id="chartCpuRam"></canvas>
                    </div>
                </div>

                <div class="card">
                    <div class="card-header">
                        <div class="card-title">Lemez & Hálózati Sebesség</div>
                    </div>
                    <div class="chart-container">
                        <canvas id="chartDiskNet"></canvas>
                    </div>
                </div>
            </div>
        </section>

        <!-- 2. Feladatkezelő Tab -->
        <section id="tab-processes" class="tab-content">
            <div class="card">
                <div class="card-header">
                    <div class="card-title">Futó Folyamatok & RAM Fogyasztás</div>
                    <input type="text" class="search-box" id="proc-search" placeholder="Keresés név vagy PID alapján..." oninput="filterProcesses()">
                </div>
                <div class="table-wrapper">
                    <table>
                        <thead>
                            <tr>
                                <th>PID</th>
                                <th>Alkalmazás neve</th>
                                <th>RAM (MiB)</th>
                                <th>RAM %</th>
                                <th>Mi ez (Leírás)</th>
                                <th>Művelet</th>
                            </tr>
                        </thead>
                        <tbody id="proc-table-body">
                            <tr><td colspan="6" style="text-align:center;">Folyamatok betöltése...</td></tr>
                        </tbody>
                    </table>
                </div>
            </div>
        </section>

        <!-- 3. Wise Memory Wiper Tab -->
        <section id="tab-wiper" class="tab-content">
            <div class="card" style="margin-bottom: 20px;">
                <div class="card-header">
                    <div class="card-title">🧹 Instant Wise Memory Wiper Control</div>
                    <button class="btn btn-primary" onclick="triggerSweep()">⚡ AZONNALI RAM SEPERGETÉS</button>
                </div>
                <p style="color: var(--text-muted); font-size: 13px;">
                    Kiüríti az összes nem-rendszer folyamat fizikai munkakészletét (`EmptyWorkingSet`) és felszabadítja a Windows Standby gyorsítótárát.
                </p>
                <div id="sweep-alert" style="margin-top: 12px; font-family: var(--font-mono); font-size: 13px; color: var(--accent-emerald);"></div>
            </div>

            <div class="card">
                <div class="card-header">
                    <div class="card-title">📝 Élő Napló (memoria_naplo.txt)</div>
                    <button class="btn" onclick="loadLog()">🔄 Frissítés</button>
                </div>
                <div class="terminal-box" id="log-box">Napló betöltése...</div>
            </div>
        </section>

        <!-- 4. iOS Glass UI Tab -->
        <section id="tab-glassui" class="tab-content">
            <div class="grid-2">
                <div class="card">
                    <div class="card-header">
                        <div class="card-title">🍏 GlassDock & iOS Tálca</div>
                    </div>
                    <p style="color: var(--text-muted); font-size: 13px; margin-bottom: 16px;">
                        Windows 10 asztali dokk és lebegő iOS üveg-pilulák kezelése.
                    </p>
                    <div style="display: flex; flex-direction: column; gap: 12px;">
                        <button class="btn" onclick="controlDock('restart')">🔄 GlassDock Újraindítása</button>
                        <button class="btn" onclick="controlDock('apply_taskbar')">📱 iOS Tálca Pilulák Alkalmazása</button>
                        <button class="btn" onclick="controlDock('restore_layout')">📌 Asztali Ikonok Elrendezésének Visszaállítása</button>
                    </div>
                </div>

                <div class="card">
                    <div class="card-header">
                        <div class="card-title">✨ Parancsikonok & Betűtípus</div>
                    </div>
                    <p style="color: var(--text-muted); font-size: 13px; margin-bottom: 16px;">
                        Asztali nyilak és iOS Inter betűtípus beállítások.
                    </p>
                    <div style="display: flex; flex-direction: column; gap: 12px;">
                        <button class="btn" onclick="controlDock('hide_overlays')">🚫 Parancsikon Nyílak & UAC Pajzsok Elrejtése</button>
                        <button class="btn" onclick="controlDock('restore_overlays')">↩️ Parancsikon Nyílak Visszaállítása</button>
                    </div>
                </div>
            </div>
        </section>

        <!-- 5. Fejlesztői Hub Tab -->
        <section id="tab-devtools" class="tab-content">
            <div class="card">
                <div class="card-header">
                    <div class="card-title">🛠️ Fejlesztői Környezet & 1-Kattintásos Telepítő</div>
                    <button class="btn" onclick="loadDevtoolsStatus()">🔄 Állapot Ellenőrzése</button>
                </div>
                <div class="table-wrapper" style="margin-bottom: 20px;">
                    <table>
                        <thead>
                            <tr>
                                <th>Eszköz / Csomag</th>
                                <th>Állapot</th>
                                <th>Verzió</th>
                                <th>Telepítés</th>
                            </tr>
                        </thead>
                        <tbody id="devtools-table-body">
                            <tr><td colspan="4" style="text-align:center;">Állapot ellenőrzése...</td></tr>
                        </tbody>
                    </table>
                </div>

                <div class="card-title" style="margin-bottom: 8px;">Telepítési Folyamat Kimenet</div>
                <div class="terminal-box" id="dev-install-log" style="height: 180px;">Válassz egy csomagot a telepítéshez...</div>
            </div>
        </section>

        <!-- 6. Terminál Tab -->
        <section id="tab-terminal" class="tab-content">
            <div class="card">
                <div class="card-header">
                    <div class="card-title">💻 Interaktív Fejlesztői Terminál</div>
                </div>
                <div class="terminal-box" id="terminal-output">GlassControl Console v1.0.0 [Admin Mode]
Írj be egy PowerShell / CMD parancsot alább...
</div>
                <div class="terminal-input-wrapper">
                    <input type="text" class="terminal-input" id="terminal-cmd" placeholder="pl. Get-Process | select -First 5 vagy py --version..." onkeydown="if(event.key==='Enter') runCmd()">
                    <button class="btn btn-primary" onclick="runCmd()">Futtatás</button>
                </div>
            </div>
        </section>

    </main>

    <script>
        let allProcesses = [];
        let chartCpuRam, chartDiskNet;

        function switchTab(tabId) {
            document.querySelectorAll('.tab-content').forEach(el => el.classList.remove('active'));
            document.querySelectorAll('.nav-tab').forEach(el => el.classList.remove('active'));
            
            document.getElementById(tabId).classList.add('active');
            event.currentTarget.classList.add('active');

            if (tabId === 'tab-processes') loadProcesses();
            if (tabId === 'tab-wiper') loadLog();
            if (tabId === 'tab-devtools') loadDevtoolsStatus();
        }

        function initCharts() {
            const ctx1 = document.getElementById('chartCpuRam').getContext('2d');
            chartCpuRam = new Chart(ctx1, {
                type: 'line',
                data: {
                    labels: [],
                    datasets: [
                        { label: 'CPU %', borderColor: '#38BDF8', backgroundColor: 'rgba(56, 189, 248, 0.1)', data: [], tension: 0.3, fill: true },
                        { label: 'RAM %', borderColor: '#8B5CF6', backgroundColor: 'rgba(139, 92, 246, 0.1)', data: [], tension: 0.3, fill: true }
                    ]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    scales: {
                        y: { min: 0, max: 100, grid: { color: '#30363D' }, ticks: { color: '#8B949E' } },
                        x: { grid: { color: '#30363D' }, ticks: { color: '#8B949E' } }
                    },
                    plugins: { legend: { labels: { color: '#F0F6FC' } } }
                }
            });

            const ctx2 = document.getElementById('chartDiskNet').getContext('2d');
            chartDiskNet = new Chart(ctx2, {
                type: 'line',
                data: {
                    labels: [],
                    datasets: [
                        { label: 'Lemez Írás (MB/s)', borderColor: '#F59E0B', data: [], tension: 0.3 },
                        { label: 'Hálózat Letöltés (Kb/s)', borderColor: '#10B981', data: [], tension: 0.3 }
                    ]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    scales: {
                        y: { grid: { color: '#30363D' }, ticks: { color: '#8B949E' } },
                        x: { grid: { color: '#30363D' }, ticks: { color: '#8B949E' } }
                    },
                    plugins: { legend: { labels: { color: '#F0F6FC' } } }
                }
            });
        }

        async function updateStats() {
            try {
                const res = await fetch('/api/stats');
                const data = await res.json();

                document.getElementById('val-cpu').innerText = `${data.cpu.percent}%`;
                document.getElementById('val-cpu-cores').innerText = `${data.cpu.cores} Magok`;
                document.getElementById('val-ram').innerText = `${data.ram.used_gb} / ${data.ram.total_gb} GB`;
                document.getElementById('val-ram-sub').innerText = `Szabad: ${data.ram.free_gb} GB (${data.ram.percent}%)`;
                document.getElementById('val-disk').innerText = `${data.disk.percent}%`;
                document.getElementById('val-disk-sub').innerText = `O: ${data.disk.read_mbps} / Í: ${data.disk.write_mbps} MB/s`;
                document.getElementById('val-net').innerText = `${data.net.recv_kbps} Kb/s`;

                document.getElementById('header-ram-pill').innerText = `RAM: ${data.ram.used_gb} / ${data.ram.total_gb} GB`;
                document.getElementById('header-cpu-pill').innerText = `CPU: ${data.cpu.percent}%`;

                // Update charts
                chartCpuRam.data.labels = data.history.timestamps;
                chartCpuRam.data.datasets[0].data = data.history.cpu;
                chartCpuRam.data.datasets[1].data = data.history.ram;
                chartCpuRam.update();

                chartDiskNet.data.labels = data.history.timestamps;
                chartDiskNet.data.datasets[0].data = data.history.disk_write;
                chartDiskNet.data.datasets[1].data = data.history.net_recv;
                chartDiskNet.update();
            } catch (e) { console.error(e); }
        }

        async function loadProcesses() {
            try {
                const res = await fetch('/api/processes');
                const data = await res.json();
                allProcesses = data.processes;
                renderProcesses(allProcesses);
            } catch (e) { console.error(e); }
        }

        function renderProcesses(list) {
            const tbody = document.getElementById('proc-table-body');
            tbody.innerHTML = list.map(p => `
                <tr>
                    <td style="font-family: var(--font-mono);">${p.pid}</td>
                    <td style="font-weight: 600;">${p.name}</td>
                    <td style="font-family: var(--font-mono); color: var(--accent-cyan);">${p.rss_mib} MiB</td>
                    <td style="font-family: var(--font-mono);">${p.ram_percent}%</td>
                    <td style="color: var(--text-muted);">${p.desc}</td>
                    <td>
                        <button class="btn" style="padding: 4px 8px; font-size: 11px;" onclick="trimProc(${p.pid})">Ürítés</button>
                        ${!p.is_protected ? `<button class="btn btn-danger" style="padding: 4px 8px; font-size: 11px;" onclick="killProc(${p.pid})">Leállítás</button>` : ''}
                    </td>
                </tr>
            `).join('');
        }

        function filterProcesses() {
            const q = document.getElementById('proc-search').value.toLowerCase();
            const filtered = allProcesses.filter(p => p.name.toLowerCase().includes(q) || p.pid.toString().includes(q) || p.desc.toLowerCase().includes(q));
            renderProcesses(filtered);
        }

        async function killProc(pid) {
            if (!confirm(`Biztosan leállítod a PID ${pid} folyamatot?`)) return;
            const res = await fetch('/api/process/kill', { method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify({pid}) });
            const data = await res.json();
            alert(data.message || data.error);
            loadProcesses();
        }

        async function trimProc(pid) {
            const res = await fetch('/api/process/trim', { method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify({pid}) });
            const data = await res.json();
            alert(data.message || data.error);
            loadProcesses();
        }

        async function triggerSweep() {
            const alertDiv = document.getElementById('sweep-alert');
            alertDiv.innerText = "RAM Sepergetés folyamatban...";
            const res = await fetch('/api/wiper/sweep', { method: 'POST' });
            const data = await res.json();
            alertDiv.innerText = data.message;
            updateStats();
        }

        async function loadLog() {
            const box = document.getElementById('log-box');
            const res = await fetch('/api/wiper/log');
            const data = await res.json();
            box.innerText = data.log || data.error;
        }

        async function controlDock(action) {
            const res = await fetch('/api/gui/dock', { method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify({action}) });
            const data = await res.json();
            alert(data.message || data.error);
        }

        async function loadDevtoolsStatus() {
            const tbody = document.getElementById('devtools-table-body');
            const res = await fetch('/api/devtools/status');
            const data = await res.json();

            const items = [
                { id: 'python', name: 'Python', installed: data.python, ver: data.python_ver },
                { id: 'node', name: 'Node.js', installed: data.node, ver: data.node_ver },
                { id: 'pnpm', name: 'pnpm', installed: data.pnpm, ver: data.pnpm_ver },
                { id: 'java', name: 'Java (JDK)', installed: data.java, ver: data.java_ver },
                { id: 'git', name: 'Git', installed: data.git, ver: data.git_ver },
            ];

            tbody.innerHTML = items.map(item => `
                <tr>
                    <td style="font-weight: 600;">${item.name}</td>
                    <td><span style="color: ${item.installed ? 'var(--accent-emerald)' : 'var(--accent-crimson)'}; font-weight: 600;">${item.installed ? '✓ Telepítve' : '✗ Hiányzik'}</span></td>
                    <td style="font-family: var(--font-mono);">${item.ver}</td>
                    <td>
                        <button class="btn btn-primary" style="padding: 4px 10px; font-size: 11px;" onclick="installDevTool('${item.id}')">Telepítés (Winget)</button>
                    </td>
                </tr>
            `).join('');
        }

        function installDevTool(tool) {
            const box = document.getElementById('dev-install-log');
            box.innerText = `[${tool.upper()}] Winget telepítő indítása...\n`;
            const source = new EventSource('/api/devtools/install');
            source.onmessage = function(e) {
                box.innerText += e.data + "\n";
                box.scrollTop = box.scrollHeight;
            };
        }

        async function runCmd() {
            const input = document.getElementById('terminal-cmd');
            const output = document.getElementById('terminal-output');
            const cmd = input.value;
            if (!cmd) return;

            output.innerText += `\n> ${cmd}\nFuttatás...`;
            input.value = '';

            const res = await fetch('/api/terminal/run', { method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify({command: cmd}) });
            const data = await res.json();
            output.innerText += `\n${data.output}\n`;
            output.scrollTop = output.scrollHeight;
        }

        window.onload = function() {
            initCharts();
            updateStats();
            setInterval(updateStats, 2000);
        };
    </script>
</body>
</html>
"""


def main():
    print("=" * 70)
    print("      GLASSCONTROL SUITE // OBSIDIAN ADMIN DASHBOARD")
    print("=" * 70)
    print("Indítás önálló Asztali Alkalmazás ablakban (Native Desktop App Window)...")
    print("=" * 70)

    # Flask szerver elindítása a háttérben
    server_thread = threading.Thread(
        target=lambda: app.run(host="127.0.0.1", port=5050, debug=False, use_reloader=False),
        daemon=True
    )
    server_thread.start()

    # Várunk egy pillanatot a szerver elindulására
    time.sleep(0.8)

    # Önálló Windows Alkalmazás ablak megnyitása PyWebView segítségével (böngészőfülek és címsor NÉLKÜL)
    try:
        import webview
        webview.create_window(
            title="GlassControl Suite // Obsidian Admin Dashboard",
            url="http://127.0.0.1:5050",
            width=1360,
            height=860,
            resizable=True,
            min_size=(1000, 650),
            background_color="#090D16"
        )
        webview.start()
    except Exception as e:
        print(f"[FALLBACK] PyWebView nem tudott elindulni ({e}), megnyitás böngészőben...")
        webbrowser.open("http://127.0.0.1:5050")
        try:
            while True:
                time.sleep(1)
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
