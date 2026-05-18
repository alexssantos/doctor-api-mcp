# down-k8s.ps1 — Remove releases Helm, namespace e cluster k3d.
#
# Uso:
#   .\scripts\ps\down-k8s.ps1          # teardown Helm (padrao)
#   .\scripts\ps\down-k8s.ps1 -K8s     # teardown kubectl raw (sem Helm uninstall)
#Requires -Version 5.1
param([switch]$K8s)

$ErrorActionPreference = 'Stop'
$CLUSTER_NAME = 'mcp-apis'
$NAMESPACE    = 'mcp-apis'

function RunInWSL([string]$Cmd) { wsl.exe -- bash -lc $Cmd }

# Validar ambiente WSL (apenas no Windows)
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    & "$PSScriptRoot\wsl-check.ps1" -Quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Ambiente nao esta pronto. Execute .\scripts\ps\wsl-check.ps1 para detalhes." -ForegroundColor Red
        exit 1
    }
}

if (-not $K8s) {
    Write-Host "Removendo Helm releases do namespace '$NAMESPACE'..." -ForegroundColor Yellow
    foreach ($release in @('produtoapi', 'precoapi', 'mcpserver', 'postgres-produto', 'postgres-preco')) {
        RunInWSL "helm status $release -n $NAMESPACE > /dev/null 2>&1"
        if ($LASTEXITCODE -eq 0) {
            RunInWSL "helm uninstall $release -n $NAMESPACE"
            Write-Host "  OK $release removido" -ForegroundColor Green
        } else {
            Write-Host "  -- $release nao encontrado — ignorando" -ForegroundColor DarkGray
        }
    }
}

Write-Host "Deletando namespace '$NAMESPACE'..." -ForegroundColor Yellow
RunInWSL "kubectl delete namespace $NAMESPACE --ignore-not-found=true"

Write-Host "Deletando cluster k3d '$CLUSTER_NAME'..." -ForegroundColor Yellow
RunInWSL "k3d cluster delete $CLUSTER_NAME"

Write-Host "Teardown completo." -ForegroundColor Green
