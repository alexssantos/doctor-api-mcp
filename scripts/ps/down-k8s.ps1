# down-k8s.ps1 — Remove namespace e cluster k3d.
# Com -Helm, desinstala releases Helm antes de deletar o namespace.
#
# Uso:
#   .\scripts\ps\down-k8s.ps1          # remove namespace + cluster
#   .\scripts\ps\down-k8s.ps1 -Helm    # idem, desinstalando Helm releases primeiro
#Requires -Version 5.1
param([switch]$Helm)

$ErrorActionPreference = 'Stop'
$CLUSTER_NAME = 'mcp-apis'
$NAMESPACE    = 'mcp-apis'

# Validar ambiente WSL (apenas no Windows)
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    & "$PSScriptRoot\wsl-check.ps1" -Quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Ambiente nao esta pronto. Execute .\scripts\ps\wsl-check.ps1 para detalhes." -ForegroundColor Red
        exit 1
    }
}

if ($Helm) {
    Write-Host "Removendo Helm releases do namespace '$NAMESPACE'..." -ForegroundColor Yellow
    foreach ($release in @('produtoapi', 'precoapi', 'postgres-produto', 'postgres-preco', 'mcpserver')) {
        helm status $release -n $NAMESPACE 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            helm uninstall $release -n $NAMESPACE
            Write-Host "  OK $release removido" -ForegroundColor Green
        } else {
            Write-Host "  -- $release nao encontrado — ignorando" -ForegroundColor DarkGray
        }
    }
}

Write-Host "Deletando namespace '$NAMESPACE'..." -ForegroundColor Yellow
kubectl delete namespace $NAMESPACE --ignore-not-found=true

Write-Host "Deletando cluster k3d '$CLUSTER_NAME'..." -ForegroundColor Yellow
k3d cluster delete $CLUSTER_NAME

Write-Host "Teardown completo." -ForegroundColor Green
