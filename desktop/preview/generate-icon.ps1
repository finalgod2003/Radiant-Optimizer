# Rebuild the original app monogram using the same type and colors as the WPF sidebar.
Add-Type -AssemblyName System.Drawing
$clientIconBitmap = [Drawing.Bitmap]::new(256, 256)
$clientIconGraphics = [Drawing.Graphics]::FromImage($clientIconBitmap)
$clientIconGraphics.Clear([Drawing.ColorTranslator]::FromHtml('#172338'))
$clientIconGraphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$clientIconFont = [Drawing.Font]::new('Segoe UI', 132, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
$clientIconBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#B1C7FC'))
$clientIconGraphics.DrawString('r.', $clientIconFont, $clientIconBrush, [Drawing.PointF]::new(46, 36))
$clientIconPng = [IO.MemoryStream]::new()
$clientIconBitmap.Save($clientIconPng, [Drawing.Imaging.ImageFormat]::Png)
$clientIconFile = [IO.File]::Create((Join-Path $PSScriptRoot 'Radiant.Desktop/App.ico'))
$clientIconWriter = [IO.BinaryWriter]::new($clientIconFile)
try {
    $clientIconWriter.Write([uint16]0)
    $clientIconWriter.Write([uint16]1)
    $clientIconWriter.Write([uint16]1)
    $clientIconWriter.Write([byte]0)
    $clientIconWriter.Write([byte]0)
    $clientIconWriter.Write([byte]0)
    $clientIconWriter.Write([byte]0)
    $clientIconWriter.Write([uint16]1)
    $clientIconWriter.Write([uint16]32)
    $clientIconWriter.Write([uint32]$clientIconPng.Length)
    $clientIconWriter.Write([uint32]22)
    $clientIconWriter.Write($clientIconPng.ToArray())
} finally {
    $clientIconWriter.Dispose()
    $clientIconPng.Dispose()
    $clientIconBrush.Dispose()
    $clientIconFont.Dispose()
    $clientIconGraphics.Dispose()
    $clientIconBitmap.Dispose()
}
