# ---------------------------------------------------------------------------
# 从 app.ico 生成 Microsoft Store / MSIX 需要的整套图标资源。
#
# 生成的尺寸（Windows 应用打包要求）：
#   Square44x44Logo.png     44×44      列表里的小图标
#   Square150x150Logo.png   150×150    开始菜单磁贴
#   Wide310x150Logo.png     310×150    宽磁贴
#   Square71x71Logo.png     71×71      小磁贴
#   Square310x310Logo.png   310×310    大磁贴
#   StoreLogo.png           50×50      商店列表
#
# 思路：把 ico 里最大的一帧等比缩放到画布中央，四周留白，
# 背景用深色（和面板默认背景一致），这样在浅色/深色主题下都清晰。
# ---------------------------------------------------------------------------
param(
    [string]$Ico = (Join-Path $PSScriptRoot '..\app.ico'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\package\Assets'),
    [string]$Background = '#1B2030'
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $Ico)) { throw "找不到图标：$Ico" }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

$Ico = (Resolve-Path $Ico).Path
$OutDir = (Resolve-Path $OutDir).Path

# 取 ico 里尺寸最大的一帧作为源图
$source = New-Object System.Drawing.Icon($Ico)
foreach ($size in @(256, 128, 64, 48, 32, 16)) {
    try {
        $candidate = New-Object System.Drawing.Icon($Ico, $size, $size)
        if ($candidate.Width -gt $source.Width) { $source.Dispose(); $source = $candidate }
        else { $candidate.Dispose() }
    }
    catch {
    }
}

$fillColor = [System.Drawing.Color]::FromArgb(255, 27, 32, 48)

function New-Asset {
    param([int]$Width, [int]$Height, [string]$Name, [double]$Padding = 0.12)

    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            # 用 FillRectangle 而不是 Clear：PowerShell 5.1 解析 Clear(Color) 重载时会抽风
            $brush = New-Object System.Drawing.SolidBrush -ArgumentList $fillColor
            try { $graphics.FillRectangle($brush, 0, 0, $Width, $Height) }
            finally { $brush.Dispose() }

            $inner = [Math]::Min($Width, $Height) * (1.0 - 2.0 * $Padding)
            $ratio = [Math]::Min($inner / $source.Width, $inner / $source.Height)
            $drawWidth = [int][Math]::Round($source.Width * $ratio)
            $drawHeight = [int][Math]::Round($source.Height * $ratio)
            $x = [int](($Width - $drawWidth) / 2)
            $y = [int](($Height - $drawHeight) / 2)

            $rect = New-Object System.Drawing.Rectangle($x, $y, $drawWidth, $drawHeight)
            $graphics.DrawIcon($source, $rect)
        }
        finally {
            $graphics.Dispose()
        }

        $path = Join-Path $OutDir $Name
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "生成 $Name  ($Width x $Height)"
    }
    finally {
        $bitmap.Dispose()
    }
}

New-Asset -Width 44  -Height 44  -Name 'Square44x44Logo.png'   -Padding 0.08
New-Asset -Width 150 -Height 150 -Name 'Square150x150Logo.png' -Padding 0.12
New-Asset -Width 71  -Height 71  -Name 'Square71x71Logo.png'   -Padding 0.10
New-Asset -Width 310 -Height 310 -Name 'Square310x310Logo.png' -Padding 0.16
New-Asset -Width 310 -Height 150 -Name 'Wide310x150Logo.png'   -Padding 0.16
New-Asset -Width 50  -Height 50  -Name 'StoreLogo.png'         -Padding 0.10

$source.Dispose()

Write-Host ''
Write-Host "完成，输出目录：$OutDir"


