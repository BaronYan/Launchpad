Add-Type -AssemblyName System.Drawing

# 橙色鲸鱼 + 速度线：透明背景，无底板。
function New-RoundPen([System.Drawing.Color]$color, [float]$width) {
  $pen = New-Object System.Drawing.Pen -ArgumentList $color, $width
  $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
  $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
  return $pen
}

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2
  $path.AddArc($x, $y, $d, $d, 180, 90)
  $path.AddArc(($x + $w - $d), $y, $d, $d, 270, 90)
  $path.AddArc(($x + $w - $d), ($y + $h - $d), $d, $d, 0, 90)
  $path.AddArc($x, ($y + $h - $d), $d, $d, 90, 90)
  $path.CloseFigure()
  return $path
}

function Draw-Whale([System.Drawing.Graphics]$g) {
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality



  $bodyRect = New-Object System.Drawing.Rectangle -ArgumentList 44, 46, 168, 168
  $bodyPath = New-Object System.Drawing.Drawing2D.GraphicsPath
  $bodyPath.AddEllipse($bodyRect)

  $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList $bodyRect, ([System.Drawing.Color]::FromArgb(255, 255, 152, 74)), ([System.Drawing.Color]::FromArgb(255, 244, 86, 0)), 90.0
  $g.FillPath($grad, $bodyPath)
  $grad.Dispose()

  # 左侧白色弧形纹理（参考图的语言）
  $clip = $g.Save()
  $g.SetClip($bodyPath)
  $pen = New-RoundPen ([System.Drawing.Color]::White) 11
  foreach ($r in 46, 62, 78) {
    $rect = New-Object System.Drawing.Rectangle -ArgumentList (140 - $r), (130 - $r), (2 * $r), (2 * $r)
    $g.DrawArc($pen, $rect, 110, 95)
  }
  $pen.Dispose()

  # 眼睛
  $white = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::White)
  $g.FillEllipse($white, 100, 80, 18, 18)
  $white.Dispose()
  $g.Restore($clip)

  # 尾巴两片
  $state = $g.Save()
  $g.TranslateTransform(210, 58)
  $g.RotateTransform(-42)
  $tailBrush = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(255, 246, 96, 6))
  $g.FillEllipse($tailBrush, -19, -10, 38, 20)
  $tailBrush.Dispose()
  $g.Restore($state)

  $state = $g.Save()
  $g.TranslateTransform(226, 94)
  $g.RotateTransform(-10)
  $tailBrush = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(255, 246, 96, 6))
  $g.FillEllipse($tailBrush, -17, -9, 34, 18)
  $tailBrush.Dispose()
  $g.Restore($state)

  # 身后的速度线
  $speedBrush = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(255, 255, 154, 77))
  foreach ($line in @(@(214,150,36,9), @(220,168,28,9), @(226,186,20,9))) {
    $path = New-RoundRect $line[0] $line[1] $line[2] $line[3] 4.5
    $g.FillPath($speedBrush, $path)
    $path.Dispose()
  }
  $speedBrush.Dispose()
}

function Render-Size([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap -ArgumentList $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.ScaleTransform(($size / 256.0), ($size / 256.0))
  Draw-Whale $g
  $g.Dispose()
  return $bmp
}

function Get-Pixels([System.Drawing.Bitmap]$bmp) {
  $w = $bmp.Width
  $h = $bmp.Height
  $stride = $w * 4
  $bytes = New-Object byte[] ($stride * $h)
  for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
      $c = $bmp.GetPixel($x, $y)
      $o = (($h - 1 - $y) * $stride) + ($x * 4)
      $bytes[$o] = $c.B
      $bytes[$o + 1] = $c.G
      $bytes[$o + 2] = $c.R
      $bytes[$o + 3] = $c.A
    }
  }
  return ,$bytes
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($s in $sizes) {
  $bmp = Render-Size $s
  $pixels = Get-Pixels $bmp
  $maskRow = [math]::Floor(($s + 31) / 32) * 4
  $mask = New-Object byte[] ($maskRow * $s)
  $images += ,@($s, $pixels, $mask)
  $bmp.Dispose()
}

$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($ms)
$w.Write([UInt16]0)
$w.Write([UInt16]1)
$w.Write([UInt16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
$blobs = @()
foreach ($img in $images) {
  $s = $img[0]
  $pixels = $img[1]
  $mask = $img[2]
  $imageSize = 40 + $pixels.Length + $mask.Length
  $blobs += ,@($pixels, $mask, $imageSize)

  $dim = if ($s -ge 256) { 0 } else { $s }
  $w.Write([byte]$dim)
  $w.Write([byte]$dim)
  $w.Write([byte]0)
  $w.Write([byte]0)
  $w.Write([UInt16]1)
  $w.Write([UInt16]32)
  $w.Write([UInt32]$imageSize)
  $w.Write([UInt32]$offset)
  $offset += $imageSize
}

for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]
  $pixels = $blobs[$i][0]
  $mask = $blobs[$i][1]
  $w.Write([UInt32]40)
  $w.Write([Int32]$s)
  $w.Write([Int32]($s * 2))
  $w.Write([UInt16]1)
  $w.Write([UInt16]32)
  $w.Write([UInt32]0)
  $w.Write([UInt32]($pixels.Length + $mask.Length))
  $w.Write([Int32]0)
  $w.Write([Int32]0)
  $w.Write([UInt32]0)
  $w.Write([UInt32]0)
  $w.Write([byte[]]$pixels)
  $w.Write($mask)
}

$w.Flush()
$out = Join-Path $PSScriptRoot '..\app.ico'
[System.IO.File]::WriteAllBytes($out, $ms.ToArray())
$w.Dispose()
$ms.Dispose()

$preview = Render-Size 256
$preview.Save((Join-Path $PSScriptRoot '..\icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()

Write-Host ("app.ico -> " + (Get-Item $out).Length + " bytes")
