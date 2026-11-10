<#
.SYNOPSIS
  Proves Match Ready holds no handle to CS2, Steam or FACEIT, using Microsoft's Sysinternals Handle tool.

.DESCRIPTION
  1. Download Handle from Microsoft: https://learn.microsoft.com/sysinternals/downloads/handle
     and put handle64.exe next to this script (or pass -HandleExe).
  2. Start CS2 (and FACEIT if you use it), and let Match Ready boost (Auto mode, or press the button).
  3. Run this script from an ADMIN PowerShell:
        powershell -ExecutionPolicy Bypass -File tools\Check-Handles.ps1
  The result is printed and saved to %LOCALAPPDATA%\MatchReady\handle-check.txt.
  This script only reads; it changes nothing.
#>
param([string]$HandleExe = (Join-Path $PSScriptRoot 'handle64.exe'))

$ErrorActionPreference = 'Stop'
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Write-Output 'Run this from an admin PowerShell (Handle needs admin to see another program''s handles).'; exit 2 }
if (-not (Test-Path $HandleExe)) { Write-Output "handle64.exe not found at $HandleExe. Download it from Microsoft (see the top of this script)."; exit 2 }

$app = Get-Process -Name MatchReady -ErrorAction SilentlyContinue
if (-not $app) { Write-Output 'Match Ready is not running. Start it (and a boost) first.'; exit 2 }
$game = Get-Process -Name cs2 -ErrorAction SilentlyContinue
$out = New-Object System.Collections.Generic.List[string]
$out.Add("Match Ready handle check, $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
$out.Add("Match Ready PID(s): $($app.Id -join ', ')   CS2 running: $([bool]$game)")
if (-not $game) { $out.Add('Note: CS2 is not running, so this only proves there are no Steam/FACEIT handles.') }

$lines = foreach ($p in $app) { & $HandleExe -accepteula -nobanner -a -p $p.Id 2>&1 }
$bad = $lines | Where-Object { $_ -match '\b(cs2|csgo|steam\w*|faceit[\w-]*)\.exe\(' }
$out.Add("Handles listed: $(@($lines).Count)")
if ($bad) {
    $out.Add('FAIL: Match Ready holds a handle to a protected program:')
    $bad | ForEach-Object { $out.Add('   ' + $_.Trim()) }
} else {
    $out.Add('PASS: no handle to cs2, csgo, steam or faceit processes.')
}

$dir = Join-Path $env:LOCALAPPDATA 'MatchReady'
New-Item -ItemType Directory -Force $dir | Out-Null
$out | Set-Content (Join-Path $dir 'handle-check.txt') -Encoding UTF8
$out
if ($bad) { exit 1 } else { exit 0 }
