#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Context,
    [switch]$BuildImage,
    [switch]$PreserveOnFailure
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot\..\..\..").Path
$driveLetter = $repoRoot.Substring(0, 1).ToLowerInvariant()
$wslRepo = "/mnt/$driveLetter" + ($repoRoot.Substring(2) -replace '\\', '/')
$arguments = @('--context', $Context)
if ($BuildImage) { $arguments += '--build-image' }
if ($PreserveOnFailure) { $arguments += '--preserve-on-failure' }
$quoted = ($arguments | ForEach-Object { "'" + ($_ -replace "'", "'\\''") + "'" }) -join ' '

$command = "cd '$wslRepo' && bash tests/cluster-lab/scripts/run-installation-matrix.sh $quoted"
wsl.exe -- bash -lc $command
exit $LASTEXITCODE
