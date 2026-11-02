<#
  Installs BG Signage for Revit 2024, 2025 and 2026 (whichever are on this PC), for the current Windows user only.
  No administrator rights are needed.

  Easiest: download BGSignage-x.y.z.zip from the GitHub Releases page, extract it, double-click Install.bat.
  From a source build instead (after "dotnet build -c Release-R2025" etc.), run from the repository root:
      powershell -ExecutionPolicy Bypass -File installer\Install.ps1
  Add -Uninstall to remove it, -Versions 2025 to pick versions. Files go to %AppData%\Autodesk\Revit\Addins\<year>.
#>
param(
    [string[]]$Versions = @('2024', '2025', '2026'),
    [switch]$Uninstall,
    [switch]$NoPause
)

$Versions = @($Versions -join ',' -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$root = Split-Path -Parent $PSScriptRoot
$addinRoot = Join-Path $env:APPDATA 'Autodesk\Revit\Addins'
$frameworks = @{ '2024' = 'net48'; '2025' = 'net8.0-windows'; '2026' = 'net8.0-windows' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
$installed = 0

function Version-Of($dll) { [System.Reflection.AssemblyName]::GetAssemblyName($dll).Version }
function Short($v) { "$($v.Major).$($v.Minor).$($v.Build)" }

foreach ($year in $Versions) {
    $yearFolder = Join-Path $addinRoot $year
    $toolFolder = Join-Path $yearFolder 'BGSignage'
    $manifest   = Join-Path $yearFolder 'BGSignage.addin'

    if ($Uninstall) {
        Remove-Item $toolFolder -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $manifest -Force -ErrorAction SilentlyContinue
        Write-Host "Revit ${year}: removed"
        continue
    }

    # Release zip: everything in R<year>\ next to this script. Source build: the two projects' bin folders.
    $fw = $frameworks[$year]
    $launcher = Get-ChildItem (Join-Path $PSScriptRoot "R$year") -Filter 'BGSignage.Launcher.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
    $engine   = Get-ChildItem (Join-Path $PSScriptRoot "R$year") -Filter 'BGSignage.Engine.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $launcher) {
        $launcher = Get-Item (Join-Path $root "src\BGSignage.Launcher\bin\Release-R$year\$fw\BGSignage.Launcher.dll") -ErrorAction SilentlyContinue
        $engine   = Get-Item (Join-Path $root "src\BGSignage.Engine\bin\Release-R$year\$fw\BGSignage.Engine.dll") -ErrorAction SilentlyContinue
    }
    if (-not $launcher -or -not $engine) { Write-Host "Revit ${year}: no build found, skipped"; continue }

    $lv = Version-Of $launcher.FullName; $ev = Version-Of $engine.FullName
    $lFolder = Join-Path $toolFolder "launcher\${stamp}_v$(Short $lv)"
    $eFolder = Join-Path $toolFolder "engine\${stamp}_v$(Short $ev)"
    New-Item -ItemType Directory -Force $lFolder, $eFolder | Out-Null
    Copy-Item $launcher.FullName $lFolder -Force
    Copy-Item $engine.FullName $eFolder -Force
    $pdb = [System.IO.Path]::ChangeExtension($engine.FullName, '.pdb')
    if (Test-Path $pdb) { Copy-Item $pdb $eFolder -Force }

    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>BG Signage</Name>
    <Assembly>BGSignage\launcher\${stamp}_v$(Short $lv)\BGSignage.Launcher.dll</Assembly>
    <AddInId>7c1f3b2e-5a4d-4e8b-9f61-2d0b8a6c4e17</AddInId>
    <FullClassName>BGSignage.Launcher.App</FullClassName>
    <VendorId>BARRGAZETAS</VendorId>
    <VendorDescription>Barr Gazetas</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
    New-Item -ItemType Directory -Force $yearFolder | Out-Null
    Set-Content -Path $manifest -Value $xml -Encoding UTF8

    # Files from a downloaded zip are marked as "from the internet"; clear that so Revit loads them.
    Get-ChildItem $toolFolder -Recurse -File | Unblock-File
    Unblock-File $manifest
    Write-Host "Revit ${year}: installed v$(Short $ev)"
    $installed++
}

Write-Host ''
if ($Uninstall) { Write-Host 'Done. BG Signage has been removed.' }
elseif ($installed -gt 0) { Write-Host 'Done. Start (or restart) Revit: Sign Designer is on the Barr Gazetas tab, Signage panel.' }
else {
    Write-Host 'Nothing was installed.' -ForegroundColor Red
    Write-Host 'Install.bat needs the R2024 / R2025 / R2026 folders beside it. Download BGSignage-x.y.z.zip from the'
    Write-Host 'Releases page (not "Source code"), right-click it, choose Extract All, then run Install.bat from the extracted folder.'
}
if (-not $NoPause) { Read-Host 'Press Enter to close' | Out-Null }
if (-not $Uninstall -and $installed -eq 0) { exit 1 }
