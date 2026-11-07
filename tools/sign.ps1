# ---------------------------------------------------------------------------
# 给 Launchpad.exe 做代码签名
#
# 用法：
#   1) 有商业证书（pfx 文件）：
#        .\tools\sign.ps1 -Exe .\Launchpad.exe -Pfx D:\certs\my.pfx -Password 证书密码
#   2) 证书已装进当前用户证书库（按主题或指纹选）：
#        .\tools\sign.ps1 -Exe .\Launchpad.exe -Subject "CN=Your Company"
#        .\tools\sign.ps1 -Exe .\Launchpad.exe -Thumbprint 1A2B3C...
#
# 说明：
#   - 优先用 signtool（需要 Windows SDK），它能带时间戳、SHA-256；
#     找不到 signtool 时退回 PowerShell 的 Set-AuthenticodeSignature（无时间戳）。
#   - 时间戳很重要：证书过期后，带时间戳的签名仍然有效。
# ---------------------------------------------------------------------------
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Pfx,
    [string]$Password,
    [string]$Subject,
    [string]$Thumbprint,
    [string]$Timestamp = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Exe)) { throw "找不到文件：$Exe" }
$Exe = (Resolve-Path $Exe).Path

function Find-SignTool {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\x64\signtool.exe'),
        (Join-Path $env:ProgramFiles 'Windows Kits\10\bin\x64\signtool.exe')
    )

    foreach ($path in $candidates) {
        if ($path -and (Test-Path $path)) { return $path }
    }

    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    return $null
}

$signtool = Find-SignTool

if ($signtool) {
    Write-Host "使用 signtool：$signtool"

    $arguments = @('sign', '/fd', 'SHA256', '/td', 'SHA256', '/tr', $Timestamp, '/v')

    if ($Pfx) {
        $arguments += @('/f', $Pfx)
        if ($Password) { $arguments += @('/p', $Password) }
    }
    elseif ($Thumbprint) {
        $arguments += @('/sha1', $Thumbprint)
    }
    elseif ($Subject) {
        $arguments += @('/n', $Subject)
    }
    else {
        $arguments += @('/a')   # 自动挑选合适的证书
    }

    $arguments += $Exe

    & $signtool @arguments
    if ($LASTEXITCODE -ne 0) { throw "signtool 签名失败：$LASTEXITCODE" }

    Write-Host ''
    Write-Host '校验签名：'
    & $signtool verify /pa /v $Exe
}
else {
    Write-Host '没有找到 signtool，改用 Set-AuthenticodeSignature（不带时间戳）'

    if ($Pfx) {
        $secure = ConvertTo-SecureString $Password -AsPlainText -Force
        $cert = Get-PfxCertificate -FilePath $Pfx -Password $secure
    }
    elseif ($Thumbprint) {
        $cert = Get-Item "Cert:\CurrentUser\My\$Thumbprint"
    }
    elseif ($Subject) {
        $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Subject } | Select-Object -First 1
    }
    else {
        $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Select-Object -First 1
    }

    if (-not $cert) { throw '找不到可用的代码签名证书（-Pfx / -Thumbprint / -Subject 都没匹配到）' }

    $signature = Set-AuthenticodeSignature -FilePath $Exe -Certificate $cert `
        -TimestampServer $Timestamp -HashAlgorithm SHA256 -ErrorAction Stop

    Write-Host ("签名结果：" + $signature.Status)
    if ($signature.Status -ne 'Valid') {
        Write-Host ("说明：" + $signature.StatusMessage)
    }
}

Write-Host ''
Write-Host '完成。'
