# port-forward.ps1 — Redireciona todas as portas dos servicos mcp-apis para localhost
# Usage: .\scripts\port-forward.ps1
# Pressione Ctrl+C para encerrar todos os port-forwards
#Requires -Version 5.1

$ErrorActionPreference = 'Stop'
$NAMESPACE       = 'mcp-apis'
$CLUSTER_CONTEXT = 'k3d-mcp-apis'

wsl.exe -- bash -lc "kubectl config use-context $CLUSTER_CONTEXT >/dev/null 2>&1"

Write-Host "Iniciando port-forwards (Ctrl+C para parar todos)..." -ForegroundColor Cyan
Write-Host ""
Write-Host "   PrecoAPI   -> http://localhost:5001/api/prices"
Write-Host "   PrecoAPI   -> http://localhost:5001/scalar/v1"
Write-Host "   ProdutoAPI -> http://localhost:5002/api/products"
Write-Host "   ProdutoAPI -> http://localhost:5002/scalar/v1"
Write-Host "   Jaeger     -> http://localhost:16686"
Write-Host "   Prometheus -> http://localhost:9090"
Write-Host "   Grafana    -> http://localhost:3000  (admin/admin)"
Write-Host "   MCP Server -> http://localhost:4000"
Write-Host ""
Write-Host "Port-forwards ativos. Aguardando Ctrl+C..." -ForegroundColor DarkGray

# precoapi/produtoapi usam pod direto (kubectl port-forward svc/ com porta 80 trava no kubectl v1.36)
# Outros servicos usam svc/ normalmente
$bashCmd = @(
    "PRECOAPI_POD=`$(kubectl get pod -n $NAMESPACE -l app=precoapi --no-headers 2>/dev/null | awk 'NR==1{print `$1}')"
    "PRODUTOAPI_POD=`$(kubectl get pod -n $NAMESPACE -l app=produtoapi --no-headers 2>/dev/null | awk 'NR==1{print `$1}')"
    "kubectl port-forward -n $NAMESPACE pod/`$PRECOAPI_POD   5001:8080    >/tmp/pf_precoapi.log   2>&1"
    "kubectl port-forward -n $NAMESPACE pod/`$PRODUTOAPI_POD 5002:8080    >/tmp/pf_produtoapi.log 2>&1"
    "kubectl port-forward -n $NAMESPACE svc/jaeger     16686:16686  >/tmp/pf_jaeger.log     2>&1"
    "kubectl port-forward -n $NAMESPACE svc/prometheus 9090:9090    >/tmp/pf_prometheus.log 2>&1"
    "kubectl port-forward -n $NAMESPACE svc/grafana    3000:3000    >/tmp/pf_grafana.log    2>&1"
    "kubectl port-forward -n $NAMESPACE svc/mcpserver  4000:4000    >/tmp/pf_mcpserver.log  2>&1"
) -join ' & '
$bashCmd = "$bashCmd & wait"

try {
    wsl.exe -- bash -lc $bashCmd
} finally {
    Write-Host ""
    Write-Host "Encerrando port-forwards..." -ForegroundColor Yellow
    wsl.exe -- bash -lc "pkill -f 'kubectl port-forward' 2>/dev/null || true" | Out-Null
    Write-Host "Port-forwards encerrados." -ForegroundColor Green
}
