# apply-patches.ps1
# Run from the extracted patch-bundle folder
# Usage: .\apply-patches.ps1

$ErrorActionPreference = "Stop"
# Try common locations - adjust if your repo is elsewhere
$possibleRoots = @(
    "C:\Git\Pawn Editor Forked\pawn editor forked",
    "C:\Git\Pawn Editor Forked",
    "C:\Git\rimworld-local-fixes\pawn editor forked"
)
$modRoot = $possibleRoots | Where-Object { Test-Path "$_\Source\PawnEditorForked" } | Select-Object -First 1
if (-not $modRoot) {
    Write-Host "ERROR: Could not find mod. Checked:" -ForegroundColor Red
    $possibleRoots | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    exit 1
}
Write-Host "Found mod at: $modRoot" -ForegroundColor Green
$filesDir = "$PSScriptRoot\files"

Write-Host "=== Applying Pawn Editor patches ===" -ForegroundColor Cyan

# Map each file to its destination
$fileMap = @{
    "PawnEditorMod.cs"          = "Source\PawnEditorForked\PawnEditorMod.cs"
    "LeftList.cs"               = "Source\PawnEditorForked\Tabs\Humanlike\Bio\LeftList.cs"
    "TopRightButtons.cs"        = "Source\PawnEditorForked\Tabs\Humanlike\Bio\TopRightButtons.cs"
    "TabWorker_Gear.cs"         = "Source\PawnEditorForked\Tabs\Humanlike\TabWorker_Gear.cs"
    "ListingMenu_Hediffs.cs"    = "Source\PawnEditorForked\Dialogs\ListingMenus\ListingMenu_Hediffs.cs"
    "CopyPaste.cs"              = "Source\PawnEditorForked\Utils\CopyPaste.cs"
    "StartingThingsManager.cs"  = "Source\PawnEditorForked\Utils\StartingThingsManager.cs"
    "LeftPanel.cs"              = "Source\PawnEditorForked\UI\LeftPanel.cs"
    "SaveLoadPatches.cs"        = "Source\PawnEditorForked\SaveLoad\SaveLoadPatches.cs"
    "UI.xml"                    = "Languages\English\Keyed\UI.xml"
}

# Verify mod root exists
if (-not (Test-Path $modRoot)) {
    Write-Host "ERROR: Mod not found at $modRoot" -ForegroundColor Red
    Write-Host "Adjust the `$modRoot variable at the top of this script." -ForegroundColor Yellow
    exit 1
}

# Copy each file
$copied = 0
foreach ($entry in $fileMap.GetEnumerator()) {
    $src = Join-Path $filesDir $entry.Key
    $dst = Join-Path $modRoot $entry.Value
    
    if (-not (Test-Path $src)) {
        Write-Host "  SKIP: $($entry.Key) not found in bundle" -ForegroundColor Yellow
        continue
    }
    
    $dstDir = Split-Path $dst -Parent
    if (-not (Test-Path $dstDir)) {
        New-Item -ItemType Directory -Path $dstDir -Force | Out-Null
    }
    
    Copy-Item $src $dst -Force
    Write-Host "  OK: $($entry.Key) -> $($entry.Value)" -ForegroundColor Green
    $copied++
}

Write-Host "`n$copied files patched.`n" -ForegroundColor Cyan

# Git commit and push - find the git root
$gitRoot = $modRoot
while ($gitRoot -and -not (Test-Path "$gitRoot\.git")) {
    $gitRoot = Split-Path $gitRoot -Parent
}
if (-not $gitRoot -or -not (Test-Path "$gitRoot\.git")) {
    Write-Host "ERROR: Not a git repo. Run 'git init' first." -ForegroundColor Red
    exit 1
}
Set-Location $gitRoot
Write-Host "Git root: $gitRoot" -ForegroundColor Cyan

Write-Host "=== Git status ===" -ForegroundColor Cyan
git status --short

Write-Host "`n=== Committing ===" -ForegroundColor Cyan
git add -A
git commit -m "fix: apply 9 bug fixes (v2.1 stability patches)

Fixes applied:
- #002 Favorite color Def mutation (LeftList.cs)
- #004 Missing body part kills pawn (ListingMenu_Hediffs.cs)
- #007 Child backstory missing on age-up (TopRightButtons.cs)
- #008 Clone loses stack/quality/color (CopyPaste.cs)
- #009 Scenario items safe iteration (StartingThingsManager.cs)
- #010 Custom xenotype NRE (TopRightButtons.cs)
- #012 Invisible weapons after load (TabWorker_Gear.cs)
- #016 Dev toolbar button via WidgetRow (PawnEditorMod.cs)
- #018 Mechs go rogue on start (LeftPanel.cs)
- Translation keys added (UI.xml)"

Write-Host "`n=== Pushing to origin ===" -ForegroundColor Cyan
git push

Write-Host "`n=== Done! ===" -ForegroundColor Green
Write-Host "Remember to recompile the DLL:" -ForegroundColor Yellow
Write-Host "  cd '$modRoot\Source\PawnEditorForked'" -ForegroundColor Yellow
Write-Host "  dotnet build -c Release" -ForegroundColor Yellow
