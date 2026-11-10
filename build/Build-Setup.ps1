# Builds dist\MatchReady-Setup.exe (and dist\MatchReady.exe for running without installing).
#   powershell -ExecutionPolicy Bypass -File build\Build-Setup.ps1 [-SkipTests]
param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$payload = Join-Path $dist 'payload'

# The .NET SDK may be a per-user install that isn't on PATH.
# A machine can have a .NET runtime without the SDK on PATH, so look for an actual 8.x SDK.
$hasSdk = (Get-Command dotnet -ErrorAction SilentlyContinue) -and (& dotnet --list-sdks 2>$null | Select-String '^8\.')
if (-not $hasSdk -and (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe")) { $env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Run([string]$what, [scriptblock]$cmd) {
    Write-Output "== $what"
    & $cmd
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

if (-not (Test-Path (Join-Path $root 'assets\MatchReady.ico'))) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Make-Icon.ps1')
}

if (-not $SkipTests) {
    Run 'Unit tests' { dotnet test (Join-Path $root 'MatchReady.sln') --nologo -v q }
}

Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $payload | Out-Null

Run 'Publish MatchReady.exe' { dotnet publish (Join-Path $root 'src\MatchReady\MatchReady.csproj') -c Release -o (Join-Path $dist 'app') --nologo -v q }
Copy-Item (Join-Path $dist 'app\MatchReady.exe') $payload
Copy-Item (Join-Path $dist 'app\MatchReady.exe') $dist -Force

Run 'Build Uninstall.exe' { dotnet build (Join-Path $root 'installer\Uninstall\Uninstall.csproj') -c Release -o (Join-Path $dist 'uninstall') --nologo -v q }
Copy-Item (Join-Path $dist 'uninstall\Uninstall.exe') $payload

Copy-Item (Join-Path $root 'README.md') $payload
Copy-Item (Join-Path $root 'docs\FACEIT-NOTES.md') $payload
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $payload 'LICENSE.txt')

Run 'Build MatchReady-Setup.exe' { dotnet build (Join-Path $root 'installer\Setup\Setup.csproj') -c Release -o (Join-Path $dist 'setup') --nologo -v q }
Copy-Item (Join-Path $dist 'setup\MatchReady-Setup.exe') $dist -Force

Remove-Item (Join-Path $dist 'app'), (Join-Path $dist 'uninstall'), (Join-Path $dist 'setup') -Recurse -Force

Write-Output ''
foreach ($f in 'MatchReady-Setup.exe', 'MatchReady.exe') {
    $p = Join-Path $dist $f
    Write-Output ("{0,-22} {1,6:N1} MB  SHA256 {2}" -f $f, ((Get-Item $p).Length / 1MB), (Get-FileHash $p -Algorithm SHA256).Hash)
}
