param([string]$Dir = 'C:\Users\chadh\Pictures\Razer App Controller 2D', [string]$Out, [int]$Size = 150)
Add-Type -AssemblyName System.Drawing
$fs = Get-ChildItem $Dir -File -Filter *.png | Sort-Object Name; $cols = 8; $rows = [math]::Ceiling($fs.Count / $cols)
$bmp = New-Object System.Drawing.Bitmap ($cols*$Size), ($rows*($Size+18)); $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::FromArgb(45,45,45))
$font = New-Object System.Drawing.Font 'Segoe UI', 8; $i = 0
foreach ($f in $fs) { $x = ($i % $cols) * $Size; $y = [math]::Floor($i / $cols) * ($Size+18); $im = [System.Drawing.Image]::FromFile($f.FullName)
  $g.DrawImage($im, $x+10, $y+4, $Size-20, $Size-20); $im.Dispose(); $g.DrawString($f.BaseName, $font, [System.Drawing.Brushes]::Silver, $x+4, $y+$Size-14); $i++ }
$bmp.Save($Out); $g.Dispose(); $bmp.Dispose()
