# Builds the AiViewer QuickLook plugin, installs it for the current user and restarts QuickLook.
# Also writes dist\QuickLook.Plugin.AiViewer.qlplugin (open it with QuickLook on another PC to install).
param([switch]$NoBuild, [switch]$NoRestart)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$name = 'QuickLook.Plugin.AiViewer'
$out = Join-Path $root 'bin\Release'

# Where QuickLook lives: Store (MSIX) version first, then the installer/portable one.
$pkg = Get-AppxPackage -Name '*QuickLook*' | Select-Object -First 1
if ($pkg) {
    $appDir = Join-Path $pkg.InstallLocation 'Package'
    $dataDir = Join-Path $env:LOCALAPPDATA "Packages\$($pkg.PackageFamilyName)\LocalCache\Roaming\pooi.moe\QuickLook"
} else {
    $exe = Get-Process QuickLook -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Path
    if (-not $exe) { $exe = "$env:LOCALAPPDATA\Programs\QuickLook\QuickLook.exe" }
    $appDir = Split-Path $exe
    $dataDir = Join-Path $env:APPDATA 'pooi.moe\QuickLook'
}
if (-not (Test-Path (Join-Path $appDir 'QuickLook.exe'))) { throw "QuickLook not found ($appDir)." }

if (-not $NoBuild) {
    Copy-Item (Join-Path $appDir 'QuickLook.Common.dll') (Join-Path $root 'lib\') -Force
    dotnet build (Join-Path $root "$name.csproj") -c Release -nologo -v q
    if ($LASTEXITCODE) { throw 'Build failed.' }
}

# Our own renamed copy of the pdfium that QuickLook's PDF viewer ships.
$pdfium = Join-Path $appDir 'QuickLook.Plugin\QuickLook.Plugin.PDFViewer\x64\pdfium.dll'
if (-not (Test-Path $pdfium)) { throw "pdfium.dll not found ($pdfium)." }
Copy-Item $pdfium (Join-Path $out 'pdfium_ai.dll') -Force

$files = 'QuickLook.Plugin.AiViewer.dll', 'QuickLook.Plugin.Metadata.config', 'pdfium_ai.dll' | ForEach-Object { Join-Path $out $_ }

# .qlplugin = zip of the plugin folder's contents.
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "$name.zip"
Compress-Archive -Path $files -DestinationPath $zip -Force
Move-Item $zip ([IO.Path]::ChangeExtension($zip, '.qlplugin')) -Force

$running = Get-Process QuickLook -ErrorAction SilentlyContinue
if ($running -and -not $NoRestart) { $running | Stop-Process -Force; Start-Sleep -Milliseconds 800 }

$target = Join-Path $dataDir "QuickLook.Plugin\$name"
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item $files $target -Force
"Installed to $target"

if (-not $NoRestart) {
    if ($pkg) {
        $appId = (Get-AppxPackageManifest $pkg).Package.Applications.Application.Id | Select-Object -First 1
        Start-Process "shell:AppsFolder\$($pkg.PackageFamilyName)!$appId"
    } else {
        Start-Process (Join-Path $appDir 'QuickLook.exe')
    }
    'QuickLook restarted.'
}
