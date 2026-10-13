# lumen-profile v5
# Lumen Terminal PowerShell-integráció. Ezt a fájlt a Terminal frissítheti; a saját kiegészítéseidet a lumen-user.ps1 fájlba írd.
$global:__lumen_first = $true; $global:__lumen_dir = $PSScriptRoot
function global:prompt {
    $ok = $?; $code = 0; if (-not $ok) { $code = 1; if ($global:LASTEXITCODE -is [int] -and $global:LASTEXITCODE -ne 0) { $code = $global:LASTEXITCODE } }
    $e = [char]27; $b = [char]7; $loc = $executionContext.SessionState.Path.CurrentLocation; $p = $loc.Path; $h = $env:USERPROFILE
    if ($p -eq $h) { $d = '~' } else { $d = Split-Path -Leaf $p; if (-not $d) { $d = $p } }
    $git = ''
    try { $dir = $p; for ($i = 0; $i -lt 12 -and $dir; $i++) { $gh = Join-Path $dir '.git\HEAD'; if (Test-Path -LiteralPath $gh) { $hd = Get-Content -LiteralPath $gh -TotalCount 1; if ($hd -match 'ref: refs/heads/(.+)') { $git = ' (' + $Matches[1] + ')' } elseif ($hd.Length -ge 7) { $git = ' (' + $hd.Substring(0, 7) + ')' }; break }; $dir = Split-Path -Parent $dir } } catch { }
    $seq = ''
    if (-not $global:__lumen_first) { $seq += "$e]133;D;$code$b" }
    $global:__lumen_first = $false
    if ($loc.Provider.Name -eq 'FileSystem') { $seq += "$e]7;file://localhost/" + ((($p -replace '\\', '/') -split '/' | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/') + $b }
    [Console]::Out.Write($seq)
    return ($env:USERNAME.ToLower() + '@' + $env:COMPUTERNAME.ToLower() + ' ' + $d + $git + ' % ')
}
try {
    Set-PSReadLineOption -BellStyle None -HistoryNoDuplicates -HistorySearchCursorMovesToEnd -MaximumHistoryCount 20000
    Set-PSReadLineKeyHandler -Key UpArrow -Function HistorySearchBackward; Set-PSReadLineKeyHandler -Key DownArrow -Function HistorySearchForward
    Set-PSReadLineKeyHandler -Key Tab -Function MenuComplete; Set-PSReadLineKeyHandler -Chord 'Ctrl+d' -Function DeleteCharOrExit
    Set-PSReadLineKeyHandler -Chord 'Ctrl+w' -Function BackwardKillWord; Set-PSReadLineKeyHandler -Chord 'Alt+d' -Function KillWord
} catch { }
# ---- navigáció
function global:.. { Set-Location .. }
function global:... { Set-Location ..\.. }
function global:.... { Set-Location ..\..\.. }
function global:~ { Set-Location ~ }
function global:cdd { if ($global:__lumen_prev) { $t = $PWD.Path; Set-Location $global:__lumen_prev; $global:__lumen_prev = $t } }
function global:mkcd { param([string]$n) New-Item -ItemType Directory -Force -Path $n | Out-Null; Set-Location $n }
function global:up { param([int]$n = 1) for ($i = 0; $i -lt $n; $i++) { Set-Location .. } }
function global:desk { Set-Location ([Environment]::GetFolderPath('Desktop')) }
function global:docs { Set-Location ([Environment]::GetFolderPath('MyDocuments')) }
function global:dl { Set-Location (Join-Path $env:USERPROFILE 'Downloads') }
# ---- fájlok
function global:ll { Get-ChildItem -Force @args | Format-Table Mode, @{n='Méret';e={if ($_.PSIsContainer) { '<DIR>' } else { '{0:N0}' -f $_.Length }};a='right'}, LastWriteTime, Name -AutoSize }
function global:la { Get-ChildItem -Force @args }
function global:l { Get-ChildItem @args }
function global:touch { param([string[]]$f) foreach ($x in $f) { if (Test-Path $x) { (Get-Item $x).LastWriteTime = Get-Date } else { New-Item -ItemType File -Path $x | Out-Null } } }
function global:which { param([string]$n) Get-Command $n -ErrorAction SilentlyContinue | Select-Object -First 1 | ForEach-Object { if ($_.Source) { $_.Source } else { $_.Definition } } }
function global:open { param([string]$p = '.') Invoke-Item -LiteralPath $p }
function global:edit { param([string]$p = '.') $c = Get-Command code, antigravity, notepad -ErrorAction SilentlyContinue | Select-Object -First 1; if ($c) { & $c.Source $p } }
function global:head { param([string]$f, [int]$n = 10) Get-Content $f -TotalCount $n }
function global:tail { param([string]$f, [int]$n = 10, [switch]$follow) Get-Content $f -Tail $n -Wait:$follow }
function global:wc { param([string]$f) $c = Get-Content $f; '{0} sor, {1} szó, {2} bájt' -f $c.Count, ($c -join ' ' -split '\s+').Count, (Get-Item $f).Length }
function global:grep { param([string]$pat, [string]$path = '.', [switch]$i) Get-ChildItem -Recurse -File -Path $path -ErrorAction SilentlyContinue | Select-String -Pattern $pat -CaseSensitive:(-not $i) }
function global:ff { param([string]$n) Get-ChildItem -Recurse -ErrorAction SilentlyContinue -Filter "*$n*" | ForEach-Object { $_.FullName } }
function global:size { param([string]$p = '.') Get-ChildItem -Force $p | ForEach-Object { $s = if ($_.PSIsContainer) { (Get-ChildItem $_.FullName -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum } else { $_.Length }; [pscustomobject]@{ Név = $_.Name; MB = [math]::Round($s / 1MB, 1) } } | Sort-Object MB -Descending }
function global:rmrf { param([string]$p) Remove-Item -Recurse -Force -LiteralPath $p }
function global:cpv { param([string]$a, [string]$b) Copy-Item -Recurse -Force $a $b -Verbose }
function global:trash { param([string]$p) Add-Type -AssemblyName Microsoft.VisualBasic; $f = Resolve-Path $p; if ((Get-Item $f).PSIsContainer) { [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($f, 'OnlyErrorDialogs', 'SendToRecycleBin') } else { [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($f, 'OnlyErrorDialogs', 'SendToRecycleBin') } }
# ---- rendszer, hálózat
function global:ports { Get-NetTCPConnection -State Listen | Sort-Object LocalPort | ForEach-Object { [pscustomobject]@{ Port = $_.LocalPort; PID = $_.OwningProcess; Program = (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName } } }
function global:killport { param([int]$p) Get-NetTCPConnection -LocalPort $p -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue; "leállítva: PID $($_.OwningProcess)" } }
function global:myip { try { (Invoke-RestMethod https://api.ipify.org) } catch { 'nincs hálózat' }; Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '169.*' -and $_.IPAddress -ne '127.0.0.1' } | ForEach-Object { 'helyi: ' + $_.IPAddress } }
function global:sysinfo { $o = Get-CimInstance Win32_OperatingSystem; $c = Get-CimInstance Win32_Processor | Select-Object -First 1; '{0}  |  {1}  |  RAM: {2:N1}/{3:N1} GB szabad' -f $o.Caption, $c.Name.Trim(), ($o.FreePhysicalMemory / 1MB), ($o.TotalVisibleMemorySize / 1MB) }
function global:path { $env:PATH -split ';' | Where-Object { $_ } }
function global:uptime { $b = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime; (Get-Date) - $b }
function global:admin { Start-Process powershell -Verb RunAs -ArgumentList '-NoExit', "-Command Set-Location '$PWD'" }
function global:reload { . (Join-Path $global:__lumen_dir 'lumen-profile.ps1') }
# ---- git
function global:gs { git status -sb @args }
function global:ga { git add @args }
function global:gaa { git add -A }
function global:gcm { git commit -m @args }
function global:gps { git push @args }
function global:gpl { git pull @args }
function global:glg { git log --oneline --graph --decorate -n 20 @args }
function global:gdf { git diff @args }
function global:gco { git checkout @args }
function global:gbr { git branch @args }
function global:gcb { git checkout -b @args }
function global:gsh { git stash @args }
function global:gundo { git reset --soft HEAD~1 }
# ---- fejlesztői eszközök
function global:serve { param([int]$p = 8000) if (Get-Command python -ErrorAction SilentlyContinue) { python -m http.server $p } else { npx serve -l $p } }
function global:json { param([Parameter(ValueFromPipeline = $true)]$i) process { $i | ConvertFrom-Json | ConvertTo-Json -Depth 20 } }
function global:b64 { param([string]$s) [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($s)) }
function global:ub64 { param([string]$s) [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($s)) }
function global:sha256 { param([string]$f) (Get-FileHash -Algorithm SHA256 $f).Hash.ToLower() }
function global:uuid { [guid]::NewGuid().ToString() }
function global:now { Get-Date -Format 'yyyy-MM-dd HH:mm:ss' }
function global:epoch { [int][double]::Parse((Get-Date -UFormat %s)) }
function global:urlenc { param([string]$s) [Uri]::EscapeDataString($s) }
function global:pn { pnpm @args }
function global:preview { param([string]$t = '') if ($t -match '^\d+$') { $t = "http://localhost:$t" } elseif ($t -and $t -notmatch '^[a-z]+://') { $t = "http://$t" }; [Console]::Out.Write("$([char]27)]777;preview;$t$([char]7)") }
function global:dev { if (Test-Path package.json) { if (Get-Command pnpm -ErrorAction SilentlyContinue) { pnpm run dev } else { npm run dev } } else { 'nincs package.json' } }
# ---- Lumen súgó
function global:lumen { param([string]$c = 'help')
    switch ($c) {
        'help' { @'
Lumen Terminal – gyorsbillentyűk: Ctrl+Shift+P parancspaletta | Ctrl+T új lap | Ctrl+Shift+D / E osztás | Ctrl+F keresés | Ctrl+R előzmények
  Ctrl+Fel/Le előző/következő parancs | Ctrl+Shift+O utolsó kimenet másolása | Ctrl+kattintás link/útvonal megnyitása
Segédparancsok: ll la .. ... mkcd up desk docs dl touch which open edit head tail wc grep ff size trash ports killport myip sysinfo path uptime admin
  gs ga gaa gcm gps gpl glg gdf gco gbr gcb gsh gundo | serve json b64 ub64 sha256 uuid now epoch urlenc dev
'@ }
        default { 'lumen help' }
    }
}
if (Test-Path (Join-Path $PSScriptRoot 'lumen-user.ps1')) { . (Join-Path $PSScriptRoot 'lumen-user.ps1') }
Clear-Host; Write-Host ('Last login: ' + (Get-Date).ToString('ddd MMM d HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture) + ' on ttys000   (lumen help)')
