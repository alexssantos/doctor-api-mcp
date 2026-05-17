# port-forward.ps1 — Redireciona todas as portas dos servicos mcp-apis para localhost
# Usage: .\scripts\port-forward.ps1
# Pressione Ctrl+C para encerrar todos os port-forwards
#Requires -Version 5.1

$ErrorActionPreference = 'Stop'
$NAMESPACE       = 'mcp-apis'
$CLUSTER_CONTEXT = 'k3d-mcp-apis'

kubectl config use-context $CLUSTER_CONTEXT 2>$null | Out-Null

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

$procs = @()

function Start-PF([string]$svc, [string]$ports) {
    Start-Process kubectl `
        -ArgumentList "port-forward -n $NAMESPACE svc/$svc $ports" `
        -PassThru -WindowStyle Hidden
}

$procs += Start-PF "precoapi"   "5001:80"
$procs += Start-PF "produtoapi" "5002:80"
$procs += Start-PF "jaeger"     "16686:16686"
$procs += Start-PF "prometheus" "9090:9090"
$procs += Start-PF "grafana"    "3000:3000"
$procs += Start-PF "mcpserver"  "4000:4000"

Write-Host "Port-forwards ativos. Aguardando Ctrl+C..." -ForegroundColor DarkGray

try {
    # Mantem o script rodando ate Ctrl+C
    while ($true) {
        Start-Sleep -Seconds 5
        # Reinicia processos que morreram
        foreach ($p in $procs) {
            if ($p.HasExited) {
                Write-Host "  Port-forward '$($p.StartInfo.Arguments)' encerrou inesperadamente." -ForegroundColor Yellow
            }
        }
    }
} finally {
    Write-Host ""
    Write-Host "Encerrando port-forwards..." -ForegroundColor Yellow
    foreach ($p in $procs) {
        try { $p.Kill() } catch {}
    }
    Write-Host "Port-forwards encerrados." -ForegroundColor Green
}
