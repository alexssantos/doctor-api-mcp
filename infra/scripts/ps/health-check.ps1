# k8s/health-check.ps1 — Verifica se todos os servicos estao up no Kubernetes
# Usage: .\scripts\ps\k8s\health-check.ps1
#Requires -Version 5.1

$ErrorActionPreference = 'Stop'

$NAMESPACE       = 'mcp-apis'
$CLUSTER_NAME    = 'mcp-apis'
$CLUSTER_CONTEXT = "k3d-$CLUSTER_NAME"

$script:PASS    = 0
$script:FAIL    = 0

function RunInWSL([string]$Cmd) { wsl.exe -- bash -lc $Cmd }
function Section($title) { Write-Host ""; Write-Host "=== $title ===" -ForegroundColor Cyan }
function Pass($msg)  { Write-Host "  [PASS] $msg" -ForegroundColor Green;  $script:PASS++ }
function Fail($msg)  { Write-Host "  [FAIL] $msg" -ForegroundColor Red;    $script:FAIL++ }
function Warn($msg)  { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }

function Stop-PF {
    wsl.exe -- bash -lc "pkill -f 'kubectl port-forward' 2>/dev/null || true" | Out-Null
}

Register-EngineEvent PowerShell.Exiting -Action { Stop-PF } | Out-Null

function Test-Http {
    param([string]$Label, [string]$Url, [int]$Expected = 200)
    try {
        $r = Invoke-WebRequest -Uri $Url -Method GET -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
        if ($r.StatusCode -eq $Expected) { Pass "$Label -> HTTP $($r.StatusCode)" }
        else                             { Fail "$Label -> esperado HTTP $Expected, obtido HTTP $($r.StatusCode)" }
    } catch {
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        if ($code -eq $Expected) { Pass "$Label -> HTTP $code" }
        else                     { Fail "$Label -> esperado HTTP $Expected, obtido HTTP $code ($Url)" }
    }
}

function Test-HttpBody {
    param([string]$Label, [string]$Url, [string]$Pattern)
    try {
        $body = (Invoke-WebRequest -Uri $Url -Method GET -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop).Content
        if ($body -match $Pattern) { Pass $Label }
        else                       { Fail "$Label (padrao '$Pattern' nao encontrado em $Url)" }
    } catch {
        Fail "$Label (sem resposta de $Url)"
    }
}

Write-Host ""
Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor White
Write-Host "║   mcp-apis — Kubernetes Health Check         ║" -ForegroundColor White
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor White

# ─── 1. Ferramentas ───────────────────────────────────────────────────────────
Section "1. Ferramentas"
foreach ($cmd in @('kubectl', 'k3d', 'curl')) {
    $found = RunInWSL "command -v $cmd 2>/dev/null"
    if ($found) { Pass "$cmd disponivel ($found)" }
    else        { Fail "$cmd nao encontrado no PATH do WSL" }
}

# ─── 2. Cluster k3d ───────────────────────────────────────────────────────────
Section "2. Cluster k3d"
$clusters = RunInWSL "k3d cluster list --no-headers 2>/dev/null" | ForEach-Object { ($_ -split '\s+')[0] }
if ($clusters -contains $CLUSTER_NAME) {
    $statusLine = RunInWSL "k3d cluster list --no-headers 2>/dev/null" | Where-Object { $_ -match "^$CLUSTER_NAME\s" }
    $servers = ($statusLine -split '\s+')[1]
    Pass "Cluster '$CLUSTER_NAME' existe (servidores: $servers)"
} else {
    Fail "Cluster '$CLUSTER_NAME' nao encontrado. Execute up-k8s.ps1"
    Write-Host ""
    Write-Host "Cluster nao encontrado. Nao e possivel continuar." -ForegroundColor Red
    Stop-PF
    exit 1
}

$null = RunInWSL "kubectl config use-context $CLUSTER_CONTEXT 2>&1"
if ($LASTEXITCODE -eq 0) { Pass "Contexto kubectl definido para '$CLUSTER_CONTEXT'" }
else                     { Fail "Falha ao definir contexto kubectl '$CLUSTER_CONTEXT'" }

# ─── 3. Namespace ─────────────────────────────────────────────────────────────
Section "3. Namespace"
$ns = RunInWSL "kubectl get namespace $NAMESPACE --no-headers 2>/dev/null"
if ($ns) { Pass "Namespace '$NAMESPACE' existe" }
else     { Fail "Namespace '$NAMESPACE' nao encontrado"; Stop-PF; exit 1 }

# ─── 4. Pods ──────────────────────────────────────────────────────────────────
Section "4. Pods"
$appLabels = @(
    'precoapi', 'produtoapi', 'mcpserver',
    'postgres-produto', 'postgres-preco',
    'jaeger', 'prometheus', 'grafana', 'loki', 'promtail'
)
foreach ($app in $appLabels) {
    $rows = RunInWSL "kubectl get pods -n $NAMESPACE -l 'app=$app' --no-headers 2>/dev/null"
    if ($rows) {
        $cols   = ($rows -split '\s+')
        $status = $cols[2]
        $ready  = $cols[1]
        if ($status -eq 'Running') { Pass "Pod $app -> Running ($ready)" }
        else                       { Fail "Pod $app -> $status ($ready)" }
    } else {
        Fail "Pod $app -> nenhum pod encontrado (label app=$app)"
    }
}

# ─── 5. Deployments e StatefulSets ────────────────────────────────────────────
Section "5. Deployments e StatefulSets"

$deployLines = RunInWSL "kubectl get deployments -n $NAMESPACE --no-headers 2>/dev/null"
foreach ($line in $deployLines) {
    if (-not $line) { continue }
    $cols    = $line -split '\s+'
    $name    = $cols[0]
    $ready   = $cols[1]   # e.g. "2/2"
    $parts   = $ready -split '/'
    if ($parts.Count -eq 2 -and $parts[0] -eq $parts[1] -and [int]$parts[1] -gt 0) {
        Pass "Deployment $name -> $ready pronto"
    } else {
        Fail "Deployment $name -> $ready (nem todos os pods prontos)"
    }
}

$stsLines = RunInWSL "kubectl get statefulsets -n $NAMESPACE --no-headers 2>/dev/null"
foreach ($line in $stsLines) {
    if (-not $line) { continue }
    $cols  = $line -split '\s+'
    $name  = $cols[0]
    $ready = $cols[1]
    $parts = $ready -split '/'
    if ($parts.Count -eq 2 -and $parts[0] -eq $parts[1] -and [int]$parts[1] -gt 0) {
        Pass "StatefulSet $name -> $ready pronto"
    } else {
        Fail "StatefulSet $name -> $ready (nem todos os pods prontos)"
    }
}

# ─── 6. Port-forwards ─────────────────────────────────────────────────────────
Section "6. Iniciando port-forwards"
Write-Host "  Aguardando servicos ficarem acessiveis..." -ForegroundColor DarkGray

$pfMap = @(
    @{ Svc = 'precoapi';   Local = 5001; Remote = 80 }
    @{ Svc = 'produtoapi'; Local = 5002; Remote = 80 }
    @{ Svc = 'mcpserver';  Local = 4000; Remote = 4000 }
    @{ Svc = 'prometheus'; Local = 9090; Remote = 9090 }
    @{ Svc = 'grafana';    Local = 3000; Remote = 3000 }
    @{ Svc = 'jaeger';     Local = 16686; Remote = 16686 }
)
$pfBash = ($pfMap | ForEach-Object {
    "kubectl port-forward -n $NAMESPACE svc/$($_.Svc) $($_.Local):$($_.Remote) >/tmp/pf_$($_.Svc).log 2>&1"
}) -join ' & '
RunInWSL "$pfBash &"
Start-Sleep -Seconds 10
Pass "$($script:PfProcs.Count) port-forward(s) iniciados"

# ─── 7. Endpoints HTTP ────────────────────────────────────────────────────────
Section "7. Endpoints HTTP"
Test-Http "PrecoAPI   /openapi/v1.json"    "http://localhost:5001/openapi/v1.json"
Test-Http "PrecoAPI   /scalar/v1"          "http://localhost:5001/scalar/v1"
Test-Http "PrecoAPI   /metrics"            "http://localhost:5001/metrics"
Test-Http "ProdutoAPI /openapi/v1.json"    "http://localhost:5002/openapi/v1.json"
Test-Http "ProdutoAPI /scalar/v1"          "http://localhost:5002/scalar/v1"
Test-Http "ProdutoAPI /metrics"            "http://localhost:5002/metrics"
Test-Http "McpServer  /health"             "http://localhost:4000/health"
Test-Http "Prometheus /api/v1/status"      "http://localhost:9090/api/v1/status/config"
Test-Http "Grafana    /api/health"         "http://localhost:3000/api/health"
Test-Http "Jaeger     UI"                  "http://localhost:16686"

# ─── 8. Conteudo dos endpoints ────────────────────────────────────────────────
Section "8. Conteudo dos endpoints"
Test-HttpBody "PrecoAPI   /metrics contem 'http_server'"  "http://localhost:5001/metrics"    "http_server"
Test-HttpBody "ProdutoAPI /metrics contem 'http_server'"  "http://localhost:5002/metrics"    "http_server"
Test-HttpBody "McpServer  /health resposta 'healthy'"     "http://localhost:4000/health"     "healthy"
Test-HttpBody "Grafana    /api/health resposta 'ok'"      "http://localhost:3000/api/health" '"ok"'

# ─── 9. Prometheus targets ────────────────────────────────────────────────────
Section "9. Prometheus targets"
try {
    $targets = Invoke-RestMethod -Uri "http://localhost:9090/api/v1/targets" -TimeoutSec 5 -ErrorAction Stop
    $upCount = ($targets.data.activeTargets | Where-Object { $_.health -eq 'up' } | Measure-Object).Count
    if ($upCount -ge 2) { Pass "Prometheus: $upCount target(s) UP" }
    else                { Fail "Prometheus: apenas $upCount target(s) UP (esperado >= 2)" }
} catch {
    Fail "Prometheus: sem resposta em /api/v1/targets"
}

# ─── 10. MCP Server — protocolo ───────────────────────────────────────────────
Section "10. MCP Server — protocolo"
try {
    $body = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"healthcheck","version":"1.0"}}}'
    $resp = Invoke-RestMethod -Uri "http://localhost:4000/" -Method POST -Body $body `
        -ContentType 'application/json' `
        -Headers @{ Accept = 'application/json, text/event-stream' } `
        -TimeoutSec 5 -ErrorAction Stop
    $json = $resp | ConvertTo-Json -Depth 5
    if ($json -match 'mcp-apis-server') { Pass "MCP initialize -> serverInfo correto" }
    else                                { Fail "MCP initialize -> resposta inesperada" }
} catch {
    Fail "MCP initialize -> sem resposta: $_"
}

# ─── Resumo ───────────────────────────────────────────────────────────────────
Stop-PF

Write-Host ""
Write-Host "══════════════════════════════════════════════" -ForegroundColor White
$total = $script:PASS + $script:FAIL
if ($script:FAIL -eq 0) {
    Write-Host "  Resultado: $($script:PASS) passou / $($script:FAIL) falhou (total: $total)" -ForegroundColor Green
} else {
    Write-Host "  Resultado: $($script:PASS) passou / $($script:FAIL) falhou (total: $total)" -ForegroundColor Yellow
}
Write-Host "══════════════════════════════════════════════" -ForegroundColor White
Write-Host ""

if ($script:FAIL -gt 0) { exit 1 }
