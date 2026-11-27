# Fills the winget manifest with the real SHA256 of a PUBLISHED release's Setup (downloads it from GitHub and hashes it).
#   powershell -ExecutionPolicy Bypass -File build\Update-Winget.ps1 -Version 1.3.1
# Then submit the three files in winget\manifests\o\onlyilir\MatchReady\<version>\ to microsoft/winget-pkgs.
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dir = Join-Path $root "winget\manifests\o\onlyilir\MatchReady\$Version"
$installer = Join-Path $dir 'onlyilir.MatchReady.installer.yaml'
if (-not (Test-Path $installer)) { throw "No manifest folder for $Version ($dir)." }

$url = "https://github.com/onlyilir/match-ready/releases/download/v$Version/MatchReady-Setup.exe"
$tmp = Join-Path ([IO.Path]::GetTempPath()) "MatchReady-Setup-$Version-winget.exe"
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
Invoke-WebRequest $url -OutFile $tmp -UseBasicParsing
$hash = (Get-FileHash $tmp -Algorithm SHA256).Hash
Remove-Item $tmp

$text = [IO.File]::ReadAllText($installer)
$text = [regex]::Replace($text, '(?m)^(\s*InstallerSha256:).*$', "`$1 $hash")
[IO.File]::WriteAllText($installer, $text)
Write-Output "InstallerSha256 for $Version = $hash"
