# validate.ps1 — Smoke tests completos: rotas de API, observabilidade e MCP server.
# Inicia port-forwards automaticamente, executa todos os testes e encerra.
#
# Uso:
#   .\scripts\ps\validate.ps1
#Requires -Version 5.1

$ErrorActionPreference = 'SilentlyContinue'
$NAMESPACE   = 'mcp-apis'
$PRECO_URL   = 'http://localhost:5001'
$PRODUTO_URL = 'http://localhost:5002'

$script:PASS    = 0
$script:FAIL    = 0
$script:PfProcs = @()

function Pass($msg)    { Write-Host "  [PASS] $msg" -ForegroundColor Green;  $script:PASS++ }
function Fail($msg)    { Write-Host "  [FAIL] $msg" -ForegroundColor Red;    $script:FAIL++ }
function Section($t)   { Write-Host ""; Write-Host "=== $t ===" -ForegroundColor Cyan }

function Stop-PF {
    foreach ($p in $script:PfProcs) { try { $p.Kill() } catch {} }
    if ($script:PfProcs.Count -gt 0) { Write-Host ""; Write-Host "Port-forwards encerrados." -ForegroundColor DarkGray }
}

function Start-PF([string]$svc, [string]$ports) {
    $proc = Start-Process kubectl -ArgumentList "port-forward -n $NAMESPACE svc/$svc $ports" `
        -PassThru -WindowStyle Hidden -ErrorAction SilentlyContinue
    if ($proc) { $script:PfProcs += $proc }
}

function Test-Http([string]$Label, [string]$Url, [int]$Expected = 200, [string]$Method = 'GET', $Body = $null) {
    try {
        $p = @{ Uri = $Url; Method = $Method; TimeoutSec = 10; UseBasicParsing = $true; ErrorAction = 'Stop' }
        if ($Body) { $p['Body'] = ($Body | ConvertTo-Json); $p['ContentType'] = 'application/json' }
        $code = [int](Invoke-WebRequest @p).StatusCode
    } catch {
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
    }
    if ($code -eq $Expected) { Pass "$Label -> HTTP $code" }
    else                     { Fail "$Label -> esperado $Expected, obtido $code" }
}

function Test-Body([string]$Label, [string]$Url, [string]$Pattern) {
    try {
        $body = (Invoke-WebRequest -Uri $Url -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop).Content
        if ($body -match $Pattern) { Pass $Label } else { Fail "$Label (padrao '$Pattern' ausente)" }
    } catch { Fail "$Label (sem resposta)" }
}

Write-Host ""
Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor White
Write-Host "║      mcp-apis  --  validate                  ║" -ForegroundColor White
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor White

kubectl config use-context k3d-mcp-apis 2>$null | Out-Null

Write-Host ""; Write-Host "Iniciando port-forwards..." -ForegroundColor DarkGray
Start-PF "precoapi"   "5001:80"
Start-PF "produtoapi" "5002:80"
Start-PF "mcpserver"  "4000:4000"
Start-PF "prometheus" "9090:9090"
Start-PF "grafana"    "3000:3000"
Start-PF "jaeger"     "16686:16686"
Start-Sleep -Seconds 6

try {

    # ─── 1. Scalar UI ─────────────────────────────────────────────────────────
    Section "1. Scalar UI"
    Test-Http "PrecoAPI   /scalar/v1"   "$PRECO_URL/scalar/v1"
    Test-Http "ProdutoAPI /scalar/v1"   "$PRODUTO_URL/scalar/v1"

    # ─── 2. CRUD de Produto e Preco ───────────────────────────────────────────
    Section "2. CRUD Produto / Preco"
    $prodId = $null
    try {
        $prod   = Invoke-RestMethod -Uri "$PRODUTO_URL/api/products" -Method POST `
            -Body (@{ name = 'Notebook Gamer'; description = '16GB RAM'; sku = 'NB-GAMER-01' } | ConvertTo-Json) `
            -ContentType 'application/json' -TimeoutSec 10 -ErrorAction Stop
        $prodId = $prod.id
        if ($prodId) { Pass "POST /api/products (id: $prodId)" } else { Fail "POST /api/products — id nulo" }
    } catch { Fail "POST /api/products: $_" }

    if ($prodId) {
        try {
            $preco = Invoke-RestMethod -Uri "$PRECO_URL/api/prices" -Method POST `
                -Body (@{ productId = $prodId; value = 4999.99; currency = 'BRL' } | ConvertTo-Json) `
                -ContentType 'application/json' -TimeoutSec 10 -ErrorAction Stop
            if ($preco.value) { Pass "POST /api/prices" } else { Fail "POST /api/prices — value nulo" }
        } catch { Fail "POST /api/prices: $_" }

        Test-Http "GET  /api/prices/$prodId"   "$PRECO_URL/api/prices/$prodId"
        Test-Http "GET  /api/products/$prodId" "$PRODUTO_URL/api/products/$prodId"

        try {
            $lista = Invoke-RestMethod -Uri "$PRODUTO_URL/api/products" -TimeoutSec 10 -ErrorAction Stop
            $json  = $lista | ConvertTo-Json -Depth 5
            if ($json -match '"value"') { Pass "GET /api/products — price integrado (PrecoAPI OK)" }
            else                        { Fail "GET /api/products — price nulo (integracao falhou)" }
        } catch { Fail "GET /api/products: $_" }

        Test-Http "DELETE /api/prices/$prodId"   "$PRECO_URL/api/prices/$prodId"   -Expected 204 -Method DELETE
        Test-Http "DELETE /api/products/$prodId" "$PRODUTO_URL/api/products/$prodId" -Expected 204 -Method DELETE
    }

    # ─── 3. Observabilidade ───────────────────────────────────────────────────
    Section "3. Observabilidade"
    Test-Body "PrecoAPI   /metrics -> http_server"   "$PRECO_URL/metrics"       "http_server"
    Test-Body "ProdutoAPI /metrics -> http_server"   "$PRODUTO_URL/metrics"     "http_server"
    Test-Http "Prometheus /api/v1/status/config"     "http://localhost:9090/api/v1/status/config"
    Test-Http "Grafana    /api/health"               "http://localhost:3000/api/health"
    Test-Http "Jaeger     /api/services"             "http://localhost:16686/api/services"

    try {
        $targets = Invoke-RestMethod -Uri "http://localhost:9090/api/v1/targets" -TimeoutSec 5 -ErrorAction Stop
        $upCount = ($targets.data.activeTargets | Where-Object { $_.health -eq 'up' } | Measure-Object).Count
        if ($upCount -ge 2) { Pass "Prometheus: $upCount target(s) UP" }
        else                { Fail "Prometheus: apenas $upCount target(s) UP (esperado >= 2)" }
    } catch { Fail "Prometheus /api/v1/targets sem resposta" }

    try {
        $cred = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes('admin:admin'))
        $ds   = Invoke-RestMethod -Uri "http://localhost:3000/api/datasources" `
            -Headers @{ Authorization = "Basic $cred" } -TimeoutSec 5 -ErrorAction Stop
        $count = ($ds | Measure-Object).Count
        if ($count -ge 3) { Pass "Grafana: $count datasource(s) configurados" }
        else              { Fail "Grafana: apenas $count datasource(s) (esperado >= 3)" }
    } catch { Fail "Grafana /api/datasources sem resposta" }

    # ─── 4. MCP Server ────────────────────────────────────────────────────────
    Section "4. MCP Server"
    Test-Body "McpServer /health -> healthy" "http://localhost:4000/health" "healthy"

    $sessionId = $null
    $toolsResp = $null
    try {
        $resp = Invoke-WebRequest -Uri "http://localhost:4000/" -Method POST `
            -Body '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"validate","version":"1.0"}}}' `
            -ContentType 'application/json' -Headers @{ Accept = 'application/json, text/event-stream' } `
            -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
        $sessionId = $resp.Headers['Mcp-Session-Id']
        if ($resp.Content -match 'mcp-apis-server') { Pass "MCP initialize -> serverInfo correto" }
        else                                         { Fail "MCP initialize -> resposta inesperada" }
    } catch { Fail "MCP initialize: $_" }

    if ($sessionId) {
        Invoke-WebRequest -Uri "http://localhost:4000/" -Method POST `
            -Body '{"jsonrpc":"2.0","method":"notifications/initialized"}' `
            -ContentType 'application/json' `
            -Headers @{ Accept = 'application/json'; 'Mcp-Session-Id' = $sessionId } `
            -TimeoutSec 5 -UseBasicParsing -ErrorAction SilentlyContinue | Out-Null

        try {
            $hdrs = @{ Accept = 'application/json, text/event-stream'; 'Mcp-Session-Id' = $sessionId }
            $resp = Invoke-WebRequest -Uri "http://localhost:4000/" -Method POST `
                -Body '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' `
                -ContentType 'application/json' -Headers $hdrs `
                -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
            $toolsResp  = $resp.Content
            $toolCount  = ([regex]::Matches($toolsResp, '"name"')).Count
            $expected   = @('list_services','get_openapi','trace_route','explain_api','get_health','find_dependencies','find_data_origin')
            $missing    = $expected | Where-Object { $toolsResp -notmatch "`"$_`"" }
            if ($toolCount -ge 7) { Pass "MCP tools/list -> $toolCount tools" } else { Fail "MCP tools/list -> $toolCount tools (esperado >= 7)" }
            if ($missing.Count -eq 0) { Pass "MCP tools — todos os nomes esperados presentes" }
            else                      { Fail "MCP tools — ausentes: $($missing -join ', ')" }
        } catch { Fail "MCP tools/list: $_" }
    }

    $ready = kubectl get pods -n $NAMESPACE -l app=mcpserver -o jsonpath='{.items[0].status.containerStatuses[0].ready}' 2>$null
    if ($ready -eq 'true') { Pass "McpServer pod ready" } else { Fail "McpServer pod nao esta ready" }

    $sa   = kubectl get serviceaccount mcp-reader     -n $NAMESPACE -o name 2>$null
    $role = kubectl get role mcp-reader-role           -n $NAMESPACE -o name 2>$null
    if ($sa -and $role) { Pass "RBAC mcp-reader configurado" } else { Fail "RBAC mcp-reader ausente" }

} finally {
    Stop-PF
}

# ─── Resumo ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "══════════════════════════════════════════════" -ForegroundColor White
$color = if ($script:FAIL -eq 0) { 'Green' } else { 'Yellow' }
Write-Host "  PASS: $($script:PASS)   FAIL: $($script:FAIL)" -ForegroundColor $color
Write-Host "══════════════════════════════════════════════" -ForegroundColor White
Write-Host ""
if ($script:FAIL -gt 0) { exit 1 }
