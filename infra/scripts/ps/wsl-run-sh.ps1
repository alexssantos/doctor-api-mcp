# wsl-run.ps1 — Executa um script .sh do projeto via WSL com PATH correto
# Equivalente PowerShell do scripts/sh/wsl-run.sh
# Usage: .\scripts\ps\wsl-run.ps1 <script.sh> [args...]
# Exemplos:
#   .\scripts\ps\wsl-run.ps1 deploy-k8s.sh
#   .\scripts\ps\wsl-run.ps1 deploy-helm.sh --capture-body
#   .\scripts\ps\wsl-run.ps1 sh/validate-routes.sh
#Requires -Version 5.1
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Script,

    [Parameter(ValueFromRemainingArguments)]
    [string[]]$ScriptArgs
)

$ErrorActionPreference = 'Stop'

# Verificar WSL disponivel
if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) {
    Write-Host "[FAIL] wsl.exe nao encontrado. Execute: wsl --install" -ForegroundColor Red
    exit 1
}

# Validar ambiente WSL (apenas no Windows)
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $checkScript = Join-Path $PSScriptRoot "wsl-check.ps1"
    if (Test-Path $checkScript) {
        & $checkScript -Quiet
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[FAIL] Ambiente WSL com problemas. Execute .\scripts\ps\wsl-check.ps1 para detalhes." -ForegroundColor Red
            exit 1
        }
    }
}

# Resolver caminho do script .sh
$repoRoot   = (Resolve-Path "$PSScriptRoot\..\..\..").Path
$driveLetter = $repoRoot.Substring(0, 1).ToLower()
$wslRepo    = "/mnt/$driveLetter" + ($repoRoot.Substring(2) -replace '\\', '/')

# Normalizar nome do script (aceita com ou sem prefixo sh/)
$scriptName = $Script -replace '^sh[/\\]', ''
$wslScript  = "$wslRepo/infra/scripts/sh/$scriptName"

$argsStr = ($ScriptArgs | ForEach-Object { "'$_'" }) -join ' '

$wslCmd = @"
export PATH="`$HOME/.local/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
cd "$wslRepo"
bash "$wslScript" $argsStr
"@

Write-Host "Executando no WSL: bash scripts/sh/$scriptName $argsStr" -ForegroundColor Cyan
Write-Host ""

wsl -- bash -c $wslCmd
exit $LASTEXITCODE
