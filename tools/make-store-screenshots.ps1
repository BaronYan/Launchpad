# ---------------------------------------------------------------------------
# 把现有截图缩放成 Microsoft Store 要求的尺寸（1920×1080，16:9）。
#
# Store 要求：至少 1 张，1366×768 或 1920×1080，比例 16:9，不超过 3840×2160。
# 输出：store\screenshots\*.png
# ---------------------------------------------------------------------------
param(
    [string]$Source = (Join-Path $PSScriptRoot '..'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\store\screenshots')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

$targetWidth = 1920
$targetHeight = 1080

function Convert-Shot {
    param([string]$File, [string]$OutName)

    $path = Join-Path $Source $File
    if (-not (Test-Path $path)) { Write-Warning "跳过（不存在）：$File"; return }

    $image = [System.Drawing.Image]::FromFile($path)
    try {
        $bitmap = New-Object System.Drawing.Bitmap($targetWidth, $targetHeight)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

                # 等比缩放 + 居中裁剪，保证不变形
                $scale = [Math]::Max($targetWidth / $image.Width, $targetHeight / $image.Height)
                $drawWidth = [int][Math]::Ceiling($image.Width * $scale)
                $drawHeight = [int][Math]::Ceiling($image.Height * $scale)
                $x = [int](($targetWidth - $drawWidth) / 2)
                $y = [int](($targetHeight - $drawHeight) / 2)

                $dest = New-Object System.Drawing.Rectangle($x, $y, $drawWidth, $drawHeight)
                $graphics.DrawImage($image, $dest)
            }
            finally {
                $graphics.Dispose()
            }

            $out = Join-Path $OutDir $OutName
            $bitmap.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Host "生成 $OutName"
        }
        finally {
            $bitmap.Dispose()
        }
    }
    finally {
        $image.Dispose()
    }
}

Convert-Shot -File 'screenshot.png' -OutName '01-launchpad.png'
Convert-Shot -File 'folder.png'     -OutName '02-folder.png'
Convert-Shot -File 'settings.png'   -OutName '03-settings.png'
Convert-Shot -File 'search.png'     -OutName '04-search.png'

Write-Host ''
Write-Host "输出目录：$OutDir"
