$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$frames=@()
foreach($size in @(16,24,32,48,64,128,256)) {
    $bitmap=New-Object Drawing.Bitmap ($size*4),($size*4)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(($size*4/64),($size*4/64))
    $path=New-Object Drawing.Drawing2D.GraphicsPath
    foreach($corner in @(@(2,2,14,14,180),@(48,2,14,14,270),@(48,48,14,14,0),@(2,48,14,14,90))) { $path.AddArc($corner[0],$corner[1],$corner[2],$corner[3],$corner[4],90) }
    $path.CloseFigure()
    $background=New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#244E80'))
    $graphics.FillPath($background,$path)
    $pen=New-Object Drawing.Pen ([Drawing.Color]::White),5
    $pen.StartCap=[Drawing.Drawing2D.LineCap]::Round;$pen.EndCap=[Drawing.Drawing2D.LineCap]::Round;$pen.LineJoin=[Drawing.Drawing2D.LineJoin]::Round
    $points=[Drawing.PointF[]]@((New-Object Drawing.PointF 13,23),(New-Object Drawing.PointF 21,44),(New-Object Drawing.PointF 31,25),(New-Object Drawing.PointF 41,44),(New-Object Drawing.PointF 49,23))
    $graphics.DrawLines($pen,$points)
    $accent=New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#70D4B6'))
    $graphics.FillEllipse($accent,45,10,9,9)
    $small=New-Object Drawing.Bitmap $size,$size
    $sg=[Drawing.Graphics]::FromImage($small);$sg.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic;$sg.DrawImage($bitmap,0,0,$size,$size)
    $stream=New-Object IO.MemoryStream;$small.Save($stream,[Drawing.Imaging.ImageFormat]::Png);$frames+=,@{Size=$size;Data=$stream.ToArray()}
    $stream.Dispose();$sg.Dispose();$small.Dispose();$graphics.Dispose();$bitmap.Dispose();$path.Dispose();$background.Dispose();$pen.Dispose();$accent.Dispose()
}
$file=[IO.File]::Create((Join-Path $PSScriptRoot 'WorkplaceOrchestrator.ico'));$writer=New-Object IO.BinaryWriter $file
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
    $offset=6+16*$frames.Count
    foreach($frame in $frames){$dimension=if($frame.Size -eq 256){0}else{$frame.Size};$writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frame.Data.Length);$writer.Write([uint32]$offset);$offset+=$frame.Data.Length}
    foreach($frame in $frames){$writer.Write([byte[]]$frame.Data)}
} finally {$writer.Dispose();$file.Dispose()}
