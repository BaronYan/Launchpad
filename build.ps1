$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) { throw 'csc.exe not found (.NET Framework 4.x required)' }

$sources = Get-ChildItem -Path (Join-Path $PSScriptRoot 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$out = Join-Path $PSScriptRoot 'Launchpad.exe'

& $csc /nologo /codepage:65001 /win32icon:"$PSScriptRoot\app.ico" /win32manifest:"$PSScriptRoot\app.manifest" /target:winexe /platform:anycpu /optimize+ /out:"$out" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    $sources

if ($LASTEXITCODE -ne 0) { throw "compile failed: $LASTEXITCODE" }
Write-Host "OK -> $out"



