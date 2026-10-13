# Lumen + GlassDock

An iOS/macOS-style "glass" shell for Windows 10, plus a memory/system toolkit. Everything is plain C# 5 / WPF, compiled with the **.NET Framework 4.8 compiler that ships with Windows**. There is no Visual Studio, no NuGet and no installer to set up.

| Component | Output | What it is |
|---|---|---|
| **Lumen** | `GlassControl.exe` | The main app. It includes the memory wiper, Task Manager (Processes / Performance / Startup), Analytics, iOS Glass UI settings, Developer Hub, an embedded Terminal tab and Help. |
| **GlassDock** | `dock\GlassDock.exe` | The dock, taskbar pills, Start menu replacement (Win key), Spotlight (Alt+Space), rounded flyouts and glass desktop icons. |
| **Terminal** | `terminal\Terminal.exe` | A standalone ConPTY terminal. Lumen embeds the same sources. |

> UI strings are written in Hungarian in source and translated at runtime via `lang\lang.tsv` (hu / en / de).

## Requirements

- Windows 10 (1809+ for ConPTY), x64
- .NET Framework 4.8 (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`)
- Optional: WebView2 Runtime (Terminal live preview)

## Quick start (downloaded from GitHub)

The three `.exe` files are included, so you can run them straight away: no install and no admin rights needed. The folder can live anywhere, because all paths are relative to the exe.

```powershell
.\GlassControl.exe /tray                  # Lumen
explorer.exe "$PWD\dock\GlassDock.exe"    # dock + Start menu + Spotlight
```

To rebuild from source, run `.\Build-All.ps1` or one of the scripts below.

If Windows SmartScreen warns about the unsigned exe, choose **More info → Run anyway**, or build it yourself. Back out of everything at any time with **iOS Glass UI → Actions → Back to original**.

## Build

```powershell
.\Build-GlassControl.ps1                 # -> GlassControl.exe  (stops a running Lumen first)
.\dock\Build.ps1                         # -> dock\GlassDock.exe (stops a running dock first)
.\terminal\Build.ps1 -KeepRunning        # -> terminal\Terminal.exe
```

Each script calls `csc.exe` directly. The source file list is in the script, so add new `.cs` files there.

## Run

```powershell
.\GlassControl.exe /tray                 # Lumen, start minimized to tray
explorer.exe "$PWD\dock\GlassDock.exe"   # dock (start via explorer so it is not a child of your shell)
```

## Source layout

```
GlassControlWpf.cs        Lumen main window, tabs (LazyHost = tab built on first open), wiper, process list
TaskPerf.cs               Task Manager: Performance graphs (sampled off the UI thread), Startup apps
GlassAnalytics*.cs        5 s memory sampling, trends, on-disk store (analytics\)
GlassUiPanel.cs           iOS Glass UI tab: writes dock\settings.ini, which the dock hot-reloads
DevHub*.cs                Developer Hub (projects, ports, tools)
HelpVault.cs/HelpContent  Help tab (searchable cards + notes)
LumenExtras.cs            Focus mode, process autopsy, Wallpaper Engine control
WiperAdvanced.cs          Advanced wiper options
Loc.cs + lang\lang.tsv    Runtime localization (key = Hungarian source string)
dock\GlassDock.cs         Dock, taskbar pills, KeyHook, StartMenu, FlyoutStyler, DeskTiles
dock\Spotlight.cs         Spotlight search (apps, files via Windows Search, actions)
dock\Theme.cs             Shared glass theme / settings.ini model
terminal\Term*.cs         VT parser (TermCore), view, tabs/host, WebView2 preview
```

## Design notes

- **Low memory.** Both apps force WPF software rendering (`RenderMode.SoftwareOnly`), which saves about 50–60 MB of GPU-driver allocations. The Terminal preview switches to hardware rendering only while a WebView2 preview is open.
  - To force hardware rendering everywhere, set `LUMEN_HW=1` / `GLASSDOCK_HW=1`, or create an empty `hwrender.txt` next to the exe.
- **No visual effects on large surfaces.** In software mode a `DropShadowEffect` or `BlurEffect` is recomputed on every repaint, so the window shadow is drawn with layered borders.
- **The UI thread never blocks.** Process enumeration, performance counters, icon extraction and file search run on the thread pool and marshal the results back with `Dispatcher.BeginInvoke`.
- **Self-trim.** When hidden, the apps run a full GC, compact the large object heap (LOH) and call `EmptyWorkingSet`.
- **The dock always restores Windows.** On exit or crash the taskbar, Start and flyouts are put back.
  - `GlassDock.exe /restore` does the same thing manually.
  - If `dock\disabled.txt` exists, the dock does not start at all. This is what **iOS Glass UI → Actions → Back to original** creates, and **Turn on** removes it.
- **Desktop icon positions are never moved.** Glass tiles only change shortcut icons. `GlassDock.exe /untile` restores the original icons.

## Test / automation switches

| App | Switch | Effect |
|---|---|---|
| Lumen | `/tab N` | Open tab N |
| Lumen | `/shot file.png /after ms` | Save a screenshot and exit |
| Lumen | `/devterm "cmd"` | Run a command in the Terminal tab |
| Lumen | `/devprev URL` | Open a Terminal preview |
| Lumen | `/autopsy PID` | Show the process autopsy for PID |
| Dock | `/restore` | Restore the original Windows shell |
| Dock | `/untile` | Restore the original desktop icons |
| Dock | `/menutest` | Start menu test |
| Dock | `/spottest "query"` | Spotlight test |
| Terminal | `/exec`, `/run`, `/preview`, `/screen`, `/dump` | Scripted runs and screen dumps |

Environment variables: `LUMEN_MEMDUMP=1` (periodic memory log), `LUMEN_TM` (Task Manager debug).

## Contributing

- Keep to the C# 5 language level, because the in-box `csc.exe` does not support newer syntax: no `$""` strings, `?.`, `=>` members or `nameof`.
- Wrap every user-visible string in `Loc.T("…")` and add its row to `lang\lang.tsv` (`hu<TAB>en<TAB>de`).
- Do not touch the user's desktop icon layout. Any shell change must have a matching restore path.
