# Headless test: renders .ai files with the built plugin to PNG (raw page, and the whole panel
# as QuickLook would show it), and prints a few pixel values. Needs an STA PowerShell 5.1.
param(
    [Parameter(Mandatory)][string]$PluginDir,
    [Parameter(Mandatory)][string[]]$Files,
    [Parameter(Mandatory)][string]$OutDir
)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$qlCommon = Get-ChildItem "C:\Program Files\WindowsApps" -Filter "QuickLook.Common.dll" -Recurse -Depth 3 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $qlCommon) { $qlCommon = Get-Item (Join-Path $PSScriptRoot "..\lib\QuickLook.Common.dll") }
[Reflection.Assembly]::LoadFrom($qlCommon.FullName) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $PluginDir "QuickLook.Plugin.AiViewer.dll")) | Out-Null
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Save-Png($source, $file) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($source))
    $fs = [IO.File]::Create($file); try { $enc.Save($fs) } finally { $fs.Close() }
}

function Pump([int]$ms) {
    $end = [DateTime]::Now.AddMilliseconds($ms)
    while ([DateTime]::Now -lt $end) {
        $frame = New-Object System.Windows.Threading.DispatcherFrame
        [System.Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke([System.Windows.Threading.DispatcherPriority]::Background,
            [Action] { $frame.Continue = $false }) | Out-Null
        [System.Windows.Threading.Dispatcher]::PushFrame($frame)
        Start-Sleep -Milliseconds 20
    }
}

$plugin = New-Object QuickLook.Plugin.AiViewer.Plugin
foreach ($f in $Files) {
    $name = [IO.Path]::GetFileNameWithoutExtension($f)
    "== $f"
    "CanHandle: " + $plugin.CanHandle($f)

    # 1. Raw render of page 1: corners must be transparent.
    $doc = New-Object QuickLook.Plugin.AiViewer.AiDocument $f
    "Pages: $($doc.PageCount)  page 1 size (pt): $($doc.GetPageSize(0))"
    $bmp = $doc.Render(0, 800)
    $doc.Dispose()
    Save-Png $bmp (Join-Path $OutDir "$name-raw.png")
    $stride = $bmp.PixelWidth * 4
    $px = New-Object byte[] ($stride * $bmp.PixelHeight)
    $bmp.CopyPixels($px, $stride, 0)
    $opaque = 0; $partial = 0; $sample = $null
    for ($i = 3; $i -lt $px.Length; $i += 4) {
        if ($px[$i] -eq 255) { $opaque++ } elseif ($px[$i] -gt 0) { $partial++; if (-not $sample) { $sample = "B$($px[$i-3]) G$($px[$i-2]) R$($px[$i-1]) A$($px[$i])" } }
    }
    "Raw $($bmp.PixelWidth)x$($bmp.PixelHeight): corner BGRA = $($px[0]),$($px[1]),$($px[2]),$($px[3]); opaque px $opaque; edge px $partial (e.g. $sample)"

    # 2. The whole panel, laid out offscreen at a QuickLook-like size.
    $ctx = New-Object QuickLook.Common.Plugin.ContextObject
    $plugin.Prepare($f, $ctx)
    "Title: '$($ctx.Title)'  preferred size: $($ctx.PreferredSize)"
    $plugin.View($f, $ctx)
    $panel = $ctx.ViewerContent
    $w = 900; $h = 650
    $panel.Measure((New-Object System.Windows.Size $w, $h)); $panel.Arrange((New-Object System.Windows.Rect 0, 0, $w, $h))
    Pump 2500
    $panel.Measure((New-Object System.Windows.Size $w, $h)); $panel.Arrange((New-Object System.Windows.Rect 0, 0, $w, $h)); $panel.UpdateLayout()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $w, $h, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($panel)
    Save-Png $rtb (Join-Path $OutDir "$name-panel.png")
    "IsBusy after render: $($ctx.IsBusy)"
    $plugin.Cleanup()
}
