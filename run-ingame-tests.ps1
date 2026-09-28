# run-ingame-tests.ps1
# Runs Pawn Editor's in-game tests end to end, without anyone playing:
#   1. builds the local test mod (InGameTests\)
#   2. copies it into RimWorld's Mods folder (it is never published)
#   3. launches RimWorld in an ISOLATED data folder, straight into a map (-quicktest)
#   4. the test mod runs every test against real pawns, writes the results and closes the game
#   5. this script prints the results and exits 0 (all passed) or 1 (anything else)
#
# It tests the Pawn Editor currently deployed in RimWorld's Mods folder, so deploy first.
#
# Two mod lists:
#   -Mods Minimal  (default) Harmony + Pawn Editor + the tests. Fast; checks our own logic.
#   -Mods Modpack  A COPY of your real game configuration (mod list and every mod's settings), plus
#                  the tests. Slow; checks Pawn Editor against the mods real players use. Refreshed
#                  on every run, so it always mirrors your current setup. Your real config is only
#                  ever read, never written.
#   -Mods Compat   Your real list FILTERED down to the mods Pawn Editor has compatibility code for,
#                  plus their dependencies, in your own load order. The targeted middle ground: tests
#                  exactly the integrations we maintain, without needing 1000+ mods to fit in video
#                  memory. The list builds itself from the [ModCompat] attributes in the source.
#
# USO: .\run-ingame-tests.ps1
#      .\run-ingame-tests.ps1 -Mods Compat
#      .\run-ingame-tests.ps1 -Mods Modpack
#      .\run-ingame-tests.ps1 -DataFolder "D:\otra\carpeta" -TimeoutMinutes 20

param(
    [ValidateSet("Minimal", "Compat", "Modpack")]
    [string]$Mods = "Minimal",
    [string]$RimWorldDir = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld",
    # Isolated profile: its own mod list, config and saves. Defaults to one folder per mod list.
    [string]$DataFolder = "",
    # 0 = default for the mod list: 10 minutes minimal, 60 with the modpack.
    [int]$TimeoutMinutes = 0,
    # Windowed at 1280x720 and running in background. Less video memory than fullscreen, and more
    # tolerant of display changes. Off by default so the modpack profile stays as close to a normal
    # launch as possible; turn it on for unattended runs or if the GPU runs out of memory.
    [switch]$Windowed
)

if (-not $DataFolder) {
    $DataFolder = switch ($Mods) {
        "Modpack" { "$env:USERPROFILE\PawnEditorTestData-Modpack" }
        "Compat"  { "$env:USERPROFILE\PawnEditorTestData-Compat" }
        default   { "$env:USERPROFILE\PawnEditorTestData" }
    }
}
if ($TimeoutMinutes -le 0) {
    $TimeoutMinutes = switch ($Mods) { "Modpack" { 60 } "Compat" { 20 } default { 10 } }
}

$realConfig  = Join-Path $env:USERPROFILE "AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config"
$testsPackageId = "segaswolf.pawneditor.ingametests"

$repo        = $PSScriptRoot
$testsModSrc = Join-Path $repo "InGameTests"
$testsProj   = Join-Path $testsModSrc "Source"
$testsModDst = Join-Path $RimWorldDir "Mods\Pawn Editor InGame Tests"
$exe         = Join-Path $RimWorldDir "RimWorldWin64.exe"
$results     = Join-Path $DataFolder "PawnEditorTestResults.txt"
$modsConfig  = Join-Path $DataFolder "Config\ModsConfig.xml"

Write-Host "=== Pawn Editor - In-Game Tests ===" -ForegroundColor Cyan

# 1. Build the test mod
Write-Host "Building test mod..." -ForegroundColor Yellow
$build = & dotnet build $testsProj -c Release 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED" -ForegroundColor Red
    $build | ForEach-Object { Write-Host $_ }
    exit 1
}

# 2. Deploy it
if (Test-Path $testsModDst) { Remove-Item $testsModDst -Recurse -Force }
New-Item -ItemType Directory -Path $testsModDst | Out-Null
Copy-Item (Join-Path $testsModSrc "About") $testsModDst -Recurse
Copy-Item (Join-Path $testsModSrc "1.6")   $testsModDst -Recurse

# ── Compat profile helpers ──────────────────────────────────────────────────────────────────────

# Compat layers that detect their mod by TYPE NAME instead of declaring [ModCompat(...)]. Having two
# mechanisms for the same job is refactor debt in itself; until they are unified, those mods are listed
# here by packageId so the Compat profile does not silently skip them.
$extraCompatTargets = @(
    "ferny.progressioneducation"
)

function Get-CompatTargets {
    # The source code is the single source of truth: whatever declares [ModCompat("...")] is tested.
    $source   = Join-Path $repo "Source\PawnEditorForked"
    $declared = Get-ChildItem $source -Recurse -Filter *.cs |
        Select-String -Pattern '\[ModCompat\(([^\]]*)\)\]' |
        ForEach-Object {
            [regex]::Matches($_.Matches[0].Groups[1].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
        }
    @($declared) + $extraCompatTargets | ForEach-Object { $_.ToLowerInvariant() } | Sort-Object -Unique
}

function Get-ModIndex {
    # packageId (lowercase) -> About.xml path, for every installed mod, local and workshop.
    $steamapps = Split-Path (Split-Path $RimWorldDir)
    $roots = @((Join-Path $RimWorldDir "Mods"), (Join-Path $steamapps "workshop\content\294100"))
    $index = @{}
    foreach ($root in ($roots | Where-Object { Test-Path $_ })) {
        foreach ($dir in Get-ChildItem $root -Directory) {
            $aboutPath = Join-Path $dir.FullName "About\About.xml"
            if (-not (Test-Path $aboutPath)) { continue }
            try { [xml]$about = Get-Content $aboutPath -Raw } catch { continue }
            $id = $about.ModMetaData.packageId
            if ($id) { $index[$id.Trim().ToLowerInvariant()] = $aboutPath }
        }
    }
    $index
}

function Get-Dependencies([string]$aboutPath) {
    try { [xml]$about = Get-Content $aboutPath -Raw } catch { return @() }
    $nodes = $about.SelectNodes("/ModMetaData/modDependencies/li/packageId | /ModMetaData/modDependenciesByVersion/v1.6/li/packageId")
    @($nodes | ForEach-Object { $_.InnerText.Trim().ToLowerInvariant() })
}

function Get-CompatKeepSet([string[]]$activeIds) {
    # Everything the Compat profile keeps: official content, Harmony/Prepatcher, Pawn Editor, every
    # compat target the user actually has active, and all of their dependencies, transitively.
    $active = @($activeIds | ForEach-Object { $_.ToLowerInvariant() })
    $index  = Get-ModIndex
    $keep   = New-Object 'System.Collections.Generic.HashSet[string]'
    $queue  = New-Object 'System.Collections.Generic.Queue[string]'

    $seeds = @($active | Where-Object { $_ -like "ludeon.*" }) +
             @("brrainz.harmony", "zetrith.prepatcher", "segaswolf.pawneditor.fork") +
             @(Get-CompatTargets | Where-Object { $active -contains $_ })
    foreach ($id in $seeds) { $queue.Enqueue($id) }

    while ($queue.Count -gt 0) {
        $id = $queue.Dequeue()
        if (-not $keep.Add($id)) { continue }
        if ($index.ContainsKey($id)) {
            foreach ($dependency in Get-Dependencies $index[$id]) { $queue.Enqueue($dependency) }
        }
    }
    , $keep
}

# 3. Mod list for the isolated profile.
if ($Mods -in @("Modpack", "Compat")) {
    # Mirror the real game's configuration: mod list AND every mod's settings, since those change
    # behaviour (Trauma and Integrity's toggles, for example). Copied fresh on every run.
    if (-not (Test-Path (Join-Path $realConfig "ModsConfig.xml"))) {
        Write-Host "No real ModsConfig.xml found in $realConfig" -ForegroundColor Red
        exit 1
    }

    $profileConfig = Join-Path $DataFolder "Config"
    if (Test-Path $profileConfig) { Remove-Item $profileConfig -Recurse -Force }
    New-Item -ItemType Directory -Path $DataFolder -Force | Out-Null

    # Seed the profile ONCE with the whole real data folder, except saved games. Mods keep caches and
    # their own data there (Faster Game Loading's texture cache, verified in its source, lives in
    # <SaveDataFolder>\FasterGameLoading), so this is what makes a test run start like a normal launch
    # instead of a cold first boot. After the seed the profile maintains its own copies; the real
    # folder is only ever read.
    $realData = Split-Path $realConfig
    $seedMark = Join-Path $DataFolder ".seeded-from-real-profile"
    if ($Mods -eq "Modpack" -and -not (Test-Path $seedMark)) {
        Write-Host "  Seeding the profile from your real data folder (first modpack run only)..." -ForegroundColor DarkGray
        robocopy $realData $DataFolder /E /XD (Join-Path $realData "Saves") /NFL /NDL /NJH /NJS /NP | Out-Null
        Set-Content -Path $seedMark -Value "Seeded $(Get-Date -Format s) from $realData"
    }

    # The mod list and every mod's settings are refreshed on every run, so they always match yours.
    if (Test-Path $profileConfig) { Remove-Item $profileConfig -Recurse -Force }
    Copy-Item $realConfig $profileConfig -Recurse

    [xml]$config = Get-Content $modsConfig
    $activeMods = $config.ModsConfigData.activeMods
    $activeIds  = @($activeMods.li)

    if ($activeIds -notcontains "segaswolf.pawneditor.fork") {
        Write-Host "Pawn Editor Forked is not active in your real mod list - nothing to test." -ForegroundColor Red
        exit 1
    }

    if ($Mods -eq "Compat") {
        # Filter the user's OWN list rather than building a new one: it is already in a load order
        # that works, so keeping only the needed entries keeps that order for free.
        $totalMods = $activeIds.Count
        $keep = Get-CompatKeepSet $activeIds
        foreach ($entry in @($activeMods.SelectNodes("li"))) {
            if (-not $keep.Contains($entry.InnerText.Trim().ToLowerInvariant())) {
                $activeMods.RemoveChild($entry) | Out-Null
            }
        }
        $activeIds = @($activeMods.SelectNodes("li") | ForEach-Object { $_.InnerText })

        $targets = Get-CompatTargets
        $present = @($targets | Where-Object { $activeIds -contains $_ })
        Write-Host "  Compat profile: $($activeIds.Count) of your $totalMods mods" -ForegroundColor DarkGray
        Write-Host "  Compat targets active: $($present -join ', ')" -ForegroundColor DarkGray
        $absent = @($targets | Where-Object { $activeIds -notcontains $_ })
        if ($absent) { Write-Host "  Not in your list (untested): $($absent -join ', ')" -ForegroundColor DarkGray }
    }
    else {
        Write-Host "  Modpack profile: $($activeIds.Count) of your mods + the tests" -ForegroundColor DarkGray
    }

    # Appended LAST so it loads after everything, Pawn Editor included.
    if ($activeIds -notcontains $testsPackageId) {
        $entry = $config.CreateElement("li")
        $entry.InnerText = $testsPackageId
        $activeMods.AppendChild($entry) | Out-Null
    }
    $config.Save($modsConfig)
}
elseif (-not (Test-Path $modsConfig)) {
    # Minimal list. Only written when missing, so it can be edited by hand afterwards.
    New-Item -ItemType Directory -Path (Split-Path $modsConfig) -Force | Out-Null
    @"
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData>
  <activeMods>
    <li>brrainz.harmony</li>
    <li>ludeon.rimworld</li>
    <li>segaswolf.pawneditor.fork</li>
    <li>segaswolf.pawneditor.ingametests</li>
  </activeMods>
</ModsConfigData>
"@ | Set-Content -Path $modsConfig -Encoding UTF8
    Write-Host "  Created isolated mod list: $modsConfig" -ForegroundColor DarkGray
}

# 3b. Optional windowed mode, written into the PROFILE's Prefs.xml (never the real one). Field names
#     verified against RimWorld's PrefsData: screenWidth, screenHeight, fullscreen, runInBackground.
if ($Windowed) {
    $prefsPath = Join-Path $DataFolder "Config\Prefs.xml"
    if (Test-Path $prefsPath) { [xml]$prefs = Get-Content $prefsPath }
    else { [xml]$prefs = "<?xml version=`"1.0`" encoding=`"utf-8`"?><PrefsData />" }

    $settings = [ordered]@{ fullscreen = "False"; screenWidth = "1280"; screenHeight = "720"; runInBackground = "True" }
    foreach ($name in $settings.Keys) {
        $node = $prefs.DocumentElement.SelectSingleNode($name)
        if (-not $node) {
            $node = $prefs.CreateElement($name)
            $prefs.DocumentElement.AppendChild($node) | Out-Null
        }
        $node.InnerText = $settings[$name]
    }

    New-Item -ItemType Directory -Path (Split-Path $prefsPath) -Force | Out-Null
    $prefs.Save($prefsPath)
    Write-Host "  Windowed 1280x720, running in background" -ForegroundColor DarkGray
}

# 4. Run. A stale results file must never be mistaken for this run's.
if (Test-Path $results) { Remove-Item $results }

Write-Host "Launching RimWorld (isolated profile)..." -ForegroundColor Yellow
$game = Start-Process -FilePath $exe -PassThru -ArgumentList @(
    "-quicktest",
    "-savedatafolder=`"$DataFolder`"",
    "-pawneditortests"
)

# Wait for the RESULTS, not for the process we launched. Some mods (Prepatcher, for one) restart the
# game while it boots. RimWorld's GenCommandLine.Restart relaunches with the SAME arguments, so the
# tests still run - in a second process, while the first one exits. Watching only the first process
# made this script give up while the tests were still on their way.
# Restart starts the new process BEFORE shutting the old one down, so there is never a moment with no
# game running; if none is left and there are no results, the run really is over.
$launchedAt = $game.StartTime
$deadline   = (Get-Date).AddMinutes($TimeoutMinutes)

function Get-TestGames {
    # Only games started by this run: never touch a RimWorld the user opened on their own.
    Get-Process -Name "RimWorldWin64" -ErrorAction SilentlyContinue |
        Where-Object { $_.StartTime -ge $launchedAt }
}

function Test-ResultsComplete {
    (Test-Path $results) -and (Select-String -Path $results -Pattern '^RESULT:' -Quiet)
}

while (-not (Test-ResultsComplete)) {
    if ((Get-Date) -gt $deadline) {
        Get-TestGames | Stop-Process -Force
        Write-Host "TIMEOUT after $TimeoutMinutes min - the game was closed by force." -ForegroundColor Red
        Write-Host "Check Player.log (and Player-prev.log if the game restarted itself):"
        Write-Host "  %USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\"
        exit 1
    }
    if (-not (Get-TestGames)) { break }
    Start-Sleep -Seconds 5
}

# 5. Report. No results file, or no RESULT line, means the run did not finish: that is a failure.
if (-not (Test-Path $results)) {
    Write-Host "NO RESULTS FILE - the tests did not run or did not finish." -ForegroundColor Red
    Write-Host "Check Player.log (and Player-prev.log if the game restarted itself):"
    Write-Host "  %USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\"
    exit 1
}

Write-Host ""
foreach ($line in Get-Content $results) {
    $color = if ($line -like "PASS*") { "Green" } elseif ($line -like "FAIL*" -or $line -like "      *") { "Red" } else { "Gray" }
    Write-Host $line -ForegroundColor $color
}

$summary = Select-String -Path $results -Pattern '^RESULT: (\d+) passed, (\d+) failed' | Select-Object -Last 1
if (-not $summary) {
    Write-Host "Results file is incomplete - the run did not finish." -ForegroundColor Red
    exit 1
}

$failed = [int]$summary.Matches[0].Groups[2].Value
if ($failed -gt 0) { exit 1 }
exit 0
