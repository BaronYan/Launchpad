# ---------------------------------------------------------------------------
# 生成一张“自签名”代码签名证书，并把它装进当前用户的信任库。
#
# 用途：本机测试签名流程，验证 exe 的签名状态显示为 Valid。
#
# ⚠️ 重要：自签名证书**不能**消除 SmartScreen / 智能应用控制的拦截。
#    系统只信任由公共 CA（DigiCert、Sectigo、SSL.com、GlobalSign 等）签发、
#    且带有足够下载信誉的证书。自签名只对本机“签名完整性校验”有效。
#
# 用法：
#   .\tools\make-selfsigned.ps1                       # 默认 CN=Launchpad (self-signed)
#   .\tools\make-selfsigned.ps1 -Subject "CN=Kevin" -Password 123456
# ---------------------------------------------------------------------------
param(
    [string]$Subject = 'CN=Launchpad (self-signed)',
    [string]$Pfx = 'launchpad-selfsigned.pfx',
    [string]$Cer = 'launchpad-selfsigned.cer',
    [string]$Password = 'launchpad',
    [int]$Years = 5
)

$ErrorActionPreference = 'Stop'

Write-Host "创建自签名证书：$Subject"

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears($Years)

Write-Host ("指纹：" + $cert.Thumbprint)

$secure = ConvertTo-SecureString $Password -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $Pfx -Password $secure | Out-Null
Export-Certificate -Cert $cert -FilePath $Cer | Out-Null

Write-Host "已导出：$Pfx / $Cer"

# 装进“受信任的根证书颁发机构”和“受信任的发布者”，这样本机校验签名才会显示 Valid
Import-Certificate -FilePath $Cer -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
Import-Certificate -FilePath $Cer -CertStoreLocation 'Cert:\CurrentUser\TrustedPublisher' | Out-Null

Write-Host ''
Write-Host '已把证书装入当前用户的 根 / 受信任发布者，本机校验会显示 Valid。'
Write-Host "签名命令： .\tools\sign.ps1 -Exe .\Launchpad.exe -Pfx $Pfx -Password $Password"
Write-Host ''
Write-Host '提醒：自签名不会让 SmartScreen / 智能应用控制放行，那需要公共 CA 证书。'
