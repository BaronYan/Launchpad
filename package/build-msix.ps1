# ---------------------------------------------------------------------------
# 把编译产物打成 MSIX 包（可直接上传 Microsoft Store，也可本地安装）。
#
# 前置条件：
#   · 已用 Visual Studio 编译出 Release 版 exe（bin\Release\net48\Launchpad.exe）
#   · 安装 Windows SDK（含 makeappx.exe），Visual Studio 安装时勾选“Windows SDK”即可
#
# 用法：
#   .\package\build-msix.ps1 `
#       -Identity "12345Publisher.Launchpad" `
#       -Publisher "CN=1A2B3C4D-1111-2222-3333-444455556666" `
#       -PublisherName "你的开发者名称"
#
# 产物：package\out\Launchpad_1.0.0.0_x64.msix
# ---------------------------------------------------------------------------
param(
    [string]$Exe = '',
    [string]$Identity = '',
    [string]$Publisher = '',
    [string]$PublisherName = '',
    [string]$DisplayName = '',
    [switch]$NonFatal,
    [string]$Version = '1.0.0.0',
    [string]$Configuration = 'Release',
    [string]$OutFile = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# 身份信息统一从 package\identity.json 读，避免每次命令行重复输入，
# 也让 Visual Studio 的“生成”直接产出正确的包。
$identityFile = Join-Path $PSScriptRoot 'identity.json'
if (Test-Path $identityFile) {
    $identityConfig = Get-Content $identityFile -Raw -Encoding UTF8 | ConvertFrom-Json

    if (-not $Identity -and $identityConfig.IdentityName) { $Identity = $identityConfig.IdentityName }
    if (-not $Publisher -and $identityConfig.Publisher) { $Publisher = $identityConfig.Publisher }
    if (-not $PublisherName -and $identityConfig.PublisherDisplayName) { $PublisherName = $identityConfig.PublisherDisplayName }
    if (-not $DisplayName -and $identityConfig.DisplayName) { $DisplayName = $identityConfig.DisplayName }
    if ($Version -eq '1.0.0.0' -and $identityConfig.Version) { $Version = $identityConfig.Version }
}

if (-not $Identity) { $Identity = 'Launchpad.Dev' }
if (-not $Publisher) { $Publisher = 'CN=Launchpad-Dev' }
if (-not $PublisherName) { $PublisherName = 'Launchpad Dev' }
if (-not $DisplayName) { $DisplayName = 'Launchpad' }

if (-not $Exe) {
    $Exe = Join-Path $root "bin\$Configuration\net48\Launchpad.exe"
}

if (-not (Test-Path $Exe)) {
    if ($NonFatal) {
        Write-Warning "找不到 exe：$Exe，跳过 MSIX 打包。"
        return
    }

    throw "找不到 exe：$Exe`n请先在 Visual Studio 里编译，或指定 -Exe 路径。"
}

# ---- 找 makeappx.exe ----
$makeappx = $null
foreach ($kit in @('C:\Program Files (x86)\Windows Kits\10\bin', 'C:\Program Files\Windows Kits\10\bin')) {
    if (-not (Test-Path $kit)) { continue }

    $found = Get-ChildItem $kit -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\makeappx.exe' } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1

    if ($found) { $makeappx = $found; break }
}

if (-not $makeappx) {
    if ($NonFatal) {
        Write-Warning '找不到 makeappx.exe，跳过 MSIX 打包。请安装 Windows SDK。'
        return
    }

    throw '找不到 makeappx.exe。请安装 Windows SDK（Visual Studio 安装器 → 单个组件 → Windows SDK）。'
}

Write-Host "makeappx: $makeappx"

# ---- 准备暂存目录 ----
$stage = Join-Path $PSScriptRoot 'staging'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

Copy-Item $Exe (Join-Path $stage 'Launchpad.exe')
Copy-Item (Join-Path $PSScriptRoot 'Assets') (Join-Path $stage 'Assets') -Recurse

# 如果 exe 依赖同目录的 dll（例如 Newtonsoft），一并复制
$exeDir = Split-Path -Parent $Exe
Get-ChildItem $exeDir -Filter *.dll -ErrorAction SilentlyContinue | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $stage $_.Name)
}

# ---- 用模板生成 AppxManifest.xml ----
$manifest = Get-Content (Join-Path $PSScriptRoot 'Package.appxmanifest') -Raw -Encoding UTF8
$manifest = $manifest.Replace('__IDENTITY__', $Identity)
$manifest = $manifest.Replace('__PUBLISHER__', $Publisher)
$manifest = $manifest.Replace('__PUBLISHER_NAME__', $PublisherName)
$manifest = $manifest.Replace('__VERSION__', $Version)
$manifest = $manifest.Replace('__DISPLAY_NAME__', $DisplayName)

[System.IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifest, (New-Object System.Text.UTF8Encoding($false)))

# ---- 打包 ----
$outDir = Join-Path $PSScriptRoot 'out'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
if (-not $OutFile) { $OutFile = Join-Path $outDir ("{0}_{1}_x64.msix" -f ($DisplayName -replace '[^\w\.\-]', '_'), $Version) }

& $makeappx pack /d $stage /p $OutFile /o
if ($LASTEXITCODE -ne 0) { throw "makeappx 打包失败：$LASTEXITCODE" }

Write-Host ''
Write-Host "已生成：$OutFile"
Write-Host ''
Write-Host '本地安装测试（需要先签名或用开发模式）：'
Write-Host "  Add-AppxPackage -Path `"$OutFile`""
Write-Host ''
Write-Host '上传 Microsoft Store：Partner Center → 你的产品 → 包 → 上传这个 .msix'




