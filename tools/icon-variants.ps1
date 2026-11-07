Add-Type -AssemblyName System.Drawing

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
function Draw-Whale([System.Drawing.Graphics]$g, [int]$variant) {
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

  if ($variant -eq 2) {
    $g.TranslateTransform(128, 128)
    $g.RotateTransform(-8)
    $g.TranslateTransform(-128, -128)
  }

  $bodyRect = New-Object System.Drawing.Rectangle -ArgumentList 44, 46, 168, 168
  $bodyPath = New-Object System.Drawing.Drawing2D.GraphicsPath
  $bodyPath.AddEllipse($bodyRect)

  $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList $bodyRect, ([System.Drawing.Color]::FromArgb(255, 255, 152, 74)), ([System.Drawing.Color]::FromArgb(255, 244, 86, 0)), 90.0
  $g.FillPath($grad, $bodyPath)
  $grad.Dispose()

  # 白色纹理：限制在身体内部
  $clip = $g.Save()
  $g.SetClip($bodyPath)
  $pen = New-RoundPen ([System.Drawing.Color]::White) 11
  foreach ($r in 58, 76, 94) {
    $rect = New-Object System.Drawing.Rectangle -ArgumentList (152 - $r), (130 - $r), (2 * $r), (2 * $r)
    $g.DrawArc($pen, $rect, 100, 110)
  }
  $pen.Dispose()

  # 眼睛
  $white = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::White)
  $g.FillEllipse($white, 100, 82, 16, 16)
  $white.Dispose()
  $g.Restore($clip)

  # 尾巴（橙色两片，在右上角）
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

  if ($variant -eq 2) {
    # 身后的速度线
    $brush = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(255, 255, 176, 105))
    foreach ($line in @(@(212,150,40,9), @(218,168,32,9), @(224,186,24,9))) {
      $path = New-RoundRect $line[0] $line[1] $line[2] $line[3] 4.5
      $g.FillPath($brush, $path)
      $path.Dispose()
    }
    $brush.Dispose()
  }

  if ($variant -eq 3) {
    # 右上角的三道斜向速度线（顺尾巴方向）
    $brush = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(255, 255, 176, 105))
    $state2 = $g.Save()
    $g.TranslateTransform(236, 130)
    $g.RotateTransform(50)
    foreach ($line in @(@(0,0,30,9), @(-14,-2,22,9), @(-26,-4,14,9))) {
      $path = New-RoundRect $line[0] $line[1] $line[2] $line[3] 4.5
      $g.FillPath($brush, $path)
      $path.Dispose()
    }
    $g.Restore($state2)
    $brush.Dispose()
  }
}

$bmp = New-Object System.Drawing.Bitmap -ArgumentList 768, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::White)
for ($i = 0; $i -lt 3; $i++) {
  $state = $g.Save()
  $g.TranslateTransform($i * 256, 0)
  Draw-Whale $g ($i + 1)
  $g.Restore($state)
}
$g.Dispose()
$bmp.Save((Join-Path $PSScriptRoot '..\icon-variants.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "ok"

