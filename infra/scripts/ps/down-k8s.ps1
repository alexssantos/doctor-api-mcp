# down-k8s.ps1 — Remove o namespace mcp-apis (todos os recursos dentro dele).
# O cluster k3d NAO e deletado; use k3d cluster delete manualmente se necessario.
#
# Uso:
#   .\infra\scripts\ps\down-k8s.ps1
#Requires -Version 5.1

$ErrorActionPreference = 'Stop'
$NAMESPACE = 'mcp-apis'

function RunInWSL([string]$Cmd) { wsl.exe -- bash -lc $Cmd }

# Validar ambiente WSL (apenas no Windows)
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    & "$PSScriptRoot\wsl-check.ps1" -Quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Ambiente nao esta pronto. Execute .\infra\scripts\ps\wsl-check.ps1 para detalhes." -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "Deletando namespace '$NAMESPACE' (e todos os recursos dentro dele)..." -ForegroundColor Yellow
RunInWSL "kubectl delete namespace $NAMESPACE --ignore-not-found=true"

Write-Host "Namespace removido. Cluster k3d permanece intacto." -ForegroundColor Green