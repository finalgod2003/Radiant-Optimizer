# Export the path-only master SVG using Windows' built-in vector renderer.
# Run: powershell -NoProfile -STA -File scripts/export-brand-assets.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$brandRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$brandPublic = Join-Path $brandRoot 'public'
$brandDirectory = Join-Path $brandPublic 'brand'
[xml]$master = Get-Content -LiteralPath (Join-Path $brandDirectory 'ro-mark.svg') -Raw
$ns = [Xml.XmlNamespaceManager]::new($master.NameTable)
$ns.AddNamespace('svg', 'http://www.w3.org/2000/svg')
$paths = @($master.SelectNodes('/svg:svg/svg:path', $ns))
$geometries = @($paths | ForEach-Object { [Windows.Media.Geometry]::Parse($_.d) })
$ink = [Windows.Media.BrushConverter]::new().ConvertFromString('#101c2b')
$ice = [Windows.Media.BrushConverter]::new().ConvertFromString($master.svg.fill)
$white = [Windows.Media.Brushes]::White
$muted = [Windows.Media.BrushConverter]::new().ConvertFromString('#9aaabd')

function Draw-Mark($Context, [double]$X, [double]$Y, [double]$Size, $Brush) {
    $Context.PushTransform([Windows.Media.TranslateTransform]::new($X, $Y))
    $Context.PushTransform([Windows.Media.ScaleTransform]::new($Size / 128, $Size / 128))
    foreach ($geometry in $geometries) { $Context.DrawGeometry($Brush, $null, $geometry) }
    $Context.Pop()
    $Context.Pop()
}

function Render-Png($Visual, [int]$Width, [int]$Height) {
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($Width, $Height, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($Visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try {
        $encoder.Save($stream)
        return ,$stream.ToArray()
    } finally { $stream.Dispose() }
}

function Icon-Png([int]$Size, [bool]$Tile, [bool]$Rounded = $true) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    if ($Tile) {
        $radius = if ($Rounded) { $Size * 0.1875 } else { 0 }
        $context.DrawRoundedRectangle($ink, $null, [Windows.Rect]::new(0, 0, $Size, $Size), $radius, $radius)
    }
    Draw-Mark $context 0 0 $Size $ice
    $context.Close()
    return ,(Render-Png $visual $Size $Size)
}

# SVG variants share the exact same geometry, with no fonts or raster data.
$pathMarkup = ($paths | ForEach-Object { '  <path d="' + $_.d + '" />' }) -join "`n"
$tileSvg = "<svg xmlns=`"http://www.w3.org/2000/svg`" viewBox=`"0 0 128 128`" fill=`"$($master.svg.fill)`">`n  <rect width=`"128`" height=`"128`" rx=`"24`" fill=`"#101c2b`" />`n$pathMarkup`n</svg>`n"
$darkSvg = "<svg xmlns=`"http://www.w3.org/2000/svg`" viewBox=`"0 0 128 128`" fill=`"#101c2b`">`n$pathMarkup`n</svg>`n"
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $brandPublic 'logo.svg'), $tileSvg, $utf8)
[IO.File]::WriteAllText((Join-Path $brandPublic 'favicon.svg'), $tileSvg, $utf8)
[IO.File]::WriteAllText((Join-Path $brandDirectory 'ro-mark-dark.svg'), $darkSvg, $utf8)
[IO.File]::WriteAllBytes((Join-Path $brandDirectory 'ro-mark.png'), (Icon-Png 512 $false))
[IO.File]::WriteAllBytes((Join-Path $brandDirectory 'app-icon.png'), (Icon-Png 512 $true))
[IO.File]::WriteAllBytes((Join-Path $brandPublic 'apple-touch-icon.png'), (Icon-Png 180 $true $false))

# PNG-compressed, 32-bit ICO frames cover browser, taskbar and Explorer sizes.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @($sizes | ForEach-Object { ,(Icon-Png $_ $true) })
$icoPath = Join-Path $brandPublic 'favicon.ico'
$icoStream = [IO.File]::Create($icoPath)
$writer = [IO.BinaryWriter]::new($icoStream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }

# A presentation sheet also makes small-size and light-background review easy.
function Draw-Label($Context, [string]$Value, [double]$X, [double]$Y, [double]$Size, $Brush) {
    $typeface = [Windows.Media.Typeface]::new('Segoe UI')
    $text = [Windows.Media.FormattedText]::new($Value, [Globalization.CultureInfo]::InvariantCulture,
        [Windows.FlowDirection]::LeftToRight, $typeface, $Size, $Brush, 1.0)
    $Context.DrawText($text, [Windows.Point]::new($X, $Y))
}
$sheet = [Windows.Media.DrawingVisual]::new()
$ctx = $sheet.RenderOpen()
$ctx.DrawRectangle($ink, $null, [Windows.Rect]::new(0, 0, 1440, 900))
Draw-Label $ctx 'R + O' 72 48 28 $white
Draw-Label $ctx 'GEOMETRIC MONOGRAM' 72 92 13 $muted
Draw-Mark $ctx 170 204 432 $ice
Draw-Label $ctx '01 / PRIMARY MARK' 72 710 13 $muted
Draw-Label $ctx 'Ice blue on midnight' 72 738 23 $white
$ctx.DrawRoundedRectangle($white, $null, [Windows.Rect]::new(780, 48, 588, 490), 18, 18)
Draw-Mark $ctx 955 115 240 $ink
Draw-Label $ctx '02 / SINGLE COLOR' 816 430 13 $ink
Draw-Label $ctx 'The same silhouette, on light.' 816 458 21 $ink
Draw-Label $ctx '03 / AT EVERY SIZE' 816 590 13 $muted
$x = 820
foreach ($size in @(16, 24, 32, 48, 64)) {
    Draw-Mark $ctx $x (700 - $size / 2) $size $ice
    Draw-Label $ctx ([string]$size + ' px') $x 758 12 $muted
    $x += 104
}
Draw-Label $ctx '#101C2B / MIDNIGHT' 72 830 12 $muted
Draw-Label $ctx '#A4C8F4 / ICE BLUE' 310 830 12 $muted
$ctx.Close()
$brandDocs = Join-Path $brandRoot 'docs/brand'
[IO.Directory]::CreateDirectory($brandDocs) | Out-Null
[IO.File]::WriteAllBytes((Join-Path $brandDocs 'logo-preview.png'), (Render-Png $sheet 1440 900))
Write-Output 'Exported SVG, transparent PNG, application PNG, Apple touch icon, nine-size ICO and preview sheet.'
