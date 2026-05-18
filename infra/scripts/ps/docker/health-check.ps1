# docker/health-check.ps1 — Verifica se todos os servicos estao rodando via Docker
# Usage: .\scripts\docker\health-check.ps1
#Requires -Version 5.1

$ErrorActionPreference = 'Stop'

$PRECO_URL     = 'http://localhost:5001'
$PRODUTO_URL   = 'http://localhost:5002'
$MCP_URL       = 'http://localhost:4000'
$PROMETHEUS_URL = 'http://localhost:9090'
$GRAFANA_URL   = 'http://localhost:3000'
$JAEGER_URL    = 'http://localhost:16686'

$script:PASS = 0
$script:FAIL = 0

function Pass($msg) { Write-Host "  [PASS] $msg" -ForegroundColor Green;  $script:PASS++ }
function Fail($msg) { Write-Host "  [FAIL] $msg" -ForegroundColor Red;    $script:FAIL++ }
function Warn($msg) { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }
function Section($title) { Write-Host ""; Write-Host "=== $title ===" -ForegroundColor Cyan }

function Test-Http {
    param([string]$Label, [string]$Url, [int]$Expected = 200)
    try {
        $resp = Invoke-WebRequest -Uri $Url -Method GET -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
        if ($resp.StatusCode -eq $Expected) { Pass "$Label -> HTTP $($resp.StatusCode)" }
        else                                { Fail "$Label -> esperado HTTP $Expected, obtido HTTP $($resp.StatusCode)" }
    } catch {
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        if ($code -eq $Expected) { Pass "$Label -> HTTP $code" }
        else                     { Fail "$Label -> esperado HTTP $Expected, obtido HTTP $code ($Url)" }
    }
}

function Test-Container {
    param([string]$Label, [string]$ImagePattern)
    $count = (docker ps --format '{{.Image}}' 2>$null | Where-Object { $_ -match $ImagePattern } | Measure-Object).Count
    if ($count -gt 0) { Pass "$Label -> $count container(s) rodando" }
    else              { Fail "$Label -> nenhum container rodando com imagem '$ImagePattern'" }
}

function Test-Image {
    param([string]$Label, [string]$Image)
    $info = docker image inspect $Image 2>$null
    if ($LASTEXITCODE -eq 0) {
        $created = ($info | ConvertFrom-Json)[0].Created.Substring(0, 10)
        Pass "$Label -> imagem presente (criada: $created)"
    } else {
        Fail "$Label -> imagem nao encontrada. Execute o build primeiro."
    }
}

Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor White
Write-Host "║     mcp-apis — Docker Health Check           ║" -ForegroundColor White
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor White

# ─── 1. Docker daemon ─────────────────────────────────────────────────────────
Section "1. Docker Daemon"
try {
    $dockerInfo = docker version --format '{{.Server.Version}}' 2>$null
    if ($LASTEXITCODE -eq 0) { Pass "Docker daemon respondendo (versao $dockerInfo)" }
    else                     { Fail "Docker daemon nao esta rodando"; exit 1 }
} catch {
    Fail "Docker daemon nao esta rodando"; exit 1
}

# ─── 2. Imagens construidas ───────────────────────────────────────────────────
Section "2. Imagens Docker"
Test-Image "PrecoAPI"   "precoapi:latest"
Test-Image "ProdutoAPI" "produtoapi:latest"
Test-Image "McpServer"  "mcpserver:latest"

# ─── 3. Containers rodando ────────────────────────────────────────────────────
Section "3. Containers em execucao"
Test-Container "PrecoAPI"         "precoapi"
Test-Container "ProdutoAPI"       "produtoapi"
Test-Container "McpServer"        "mcpserver"
Test-Container "PostgreSQL"       "postgres"
Test-Container "Jaeger"           "jaegertracing"
Test-Container "Prometheus"       "prom/prometheus"
Test-Container "Grafana"          "grafana/grafana"

# ─── 4. Endpoints HTTP ────────────────────────────────────────────────────────
Section "4. Endpoints HTTP"
Test-Http "PrecoAPI   /metrics"      "$PRECO_URL/metrics"
Test-Http "PrecoAPI   /scalar/v1"    "$PRECO_URL/scalar/v1"
Test-Http "ProdutoAPI /metrics"      "$PRODUTO_URL/metrics"
Test-Http "ProdutoAPI /scalar/v1"    "$PRODUTO_URL/scalar/v1"
Test-Http "McpServer  /health"       "$MCP_URL/health"
Test-Http "Prometheus /api/v1/status" "$PROMETHEUS_URL/api/v1/status/config"
Test-Http "Grafana    /api/health"   "$GRAFANA_URL/api/health"
Test-Http "Jaeger     UI"            "$JAEGER_URL"

# ─── 5. Integracao PrecoAPI -> ProdutoAPI ─────────────────────────────────────
Section "5. Integracao entre servicos"
try {
    $produtos = Invoke-RestMethod -Uri "$PRODUTO_URL/api/products" -Method GET -TimeoutSec 5 -ErrorAction Stop
    Pass "ProdutoAPI GET /api/products respondendo"
    $json = $produtos | ConvertTo-Json -Depth 5
    if ($json -match '"value"') { Pass "Integracao ProdutoAPI -> PrecoAPI: campo 'value' presente" }
    else                        { Warn "Integracao ProdutoAPI -> PrecoAPI: campo 'value' ausente (sem dados ou PrecoAPI offline)" }
} catch {
    Fail "ProdutoAPI GET /api/products sem resposta"
}

# ─── 6. MCP Server — initialize ──────────────────────────────────────────────
Section "6. MCP Server — tools"
try {
    $body = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"healthcheck","version":"1.0"}}}'
    $resp = Invoke-RestMethod -Uri "$MCP_URL/" -Method POST -Body $body `
        -ContentType 'application/json' `
        -Headers @{ Accept = 'application/json, text/event-stream' } `
        -TimeoutSec 5 -ErrorAction Stop
    $json = $resp | ConvertTo-Json -Depth 5
    if ($json -match 'mcp-apis-server') { Pass "MCP Server initialize respondendo" }
    else                                { Fail "MCP Server initialize sem resposta esperada" }
} catch {
    Fail "MCP Server initialize sem resposta: $_"
}

# ─── Resumo ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "══════════════════════════════════════════════" -ForegroundColor White
$total = $script:PASS + $script:FAIL
Write-Host "  Resultado: " -NoNewline
Write-Host "$($script:PASS) passou" -ForegroundColor Green -NoNewline
Write-Host " / " -NoNewline
Write-Host "$($script:FAIL) falhou" -ForegroundColor Red -NoNewline
Write-Host " (total: $total)"
Write-Host "══════════════════════════════════════════════" -ForegroundColor White
Write-Host ""

if ($script:FAIL -gt 0) { exit 1 }
