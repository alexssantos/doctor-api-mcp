# up-k8s.ps1 — Valida e levanta o ambiente Kubernetes (k3d) completo
# Cada etapa verifica o estado atual antes de agir: ja existindo, pula.
# Ao final executa health-check completo com port-forwards.
#
# Uso:
#   .\scripts\ps\up-k8s.ps1                   # deploy + health-check
#   .\scripts\ps\up-k8s.ps1 -SkipBuild        # pula build das imagens Docker
#   .\scripts\ps\up-k8s.ps1 -SkipHealthCheck  # pula verificacao final
#   .\scripts\ps\up-k8s.ps1 -CaptureBody      # habilita captura de body no OTEL
#Requires -Version 5.1
param(
    [switch]$CaptureBody,
    [switch]$SkipBuild,
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = 'Stop'

$NAMESPACE       = 'mcp-apis'
$CLUSTER_NAME    = 'mcp-apis'
$CLUSTER_CONTEXT = "k3d-$CLUSTER_NAME"
$REPO_ROOT       = (Resolve-Path "$PSScriptRoot\..\..").Path
$driveLetter     = $REPO_ROOT.Substring(0,1).ToLower()
$WSL_REPO        = "/mnt/$driveLetter" + ($REPO_ROOT.Substring(2) -replace '\\', '/')

$script:PASS     = 0
$script:FAIL     = 0
$script:PfProcs  = @()

function RunInWSL([string]$Cmd) { wsl.exe -- bash -lc $Cmd }
function Banner($msg) { Write-Host ""; Write-Host "══► $msg" -ForegroundColor Cyan }
function Ok($msg)     { Write-Host "    [OK]   $msg" -ForegroundColor Green;  $script:PASS++ }
function Warn($msg)   { Write-Host "    [WARN] $msg" -ForegroundColor Yellow }
function Info($msg)   { Write-Host "    ...    $msg" -ForegroundColor DarkGray }
function Err($msg)    { Write-Host ""; Write-Host "[ERRO] $msg`n" -ForegroundColor Red; Stop-PF; exit 1 }
function Pass($msg)   { Write-Host "    [PASS] $msg" -ForegroundColor Green;  $script:PASS++ }
function Fail($msg)   { Write-Host "    [FAIL] $msg" -ForegroundColor Red;    $script:FAIL++ }

function Stop-PF {
    foreach ($p in $script:PfProcs) { try { $p.Kill() } catch {} }
}

Register-EngineEvent PowerShell.Exiting -Action { Stop-PF } | Out-Null

Write-Host ""
Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor White
Write-Host "║      mcp-apis  --  up-k8s                    ║" -ForegroundColor White
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor White

# ─── 0. Validar ambiente WSL (Windows only) ───────────────────────────────────
Banner "0. Validando ambiente WSL"
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    & "$PSScriptRoot\wsl-check.ps1" -Quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "Ambiente WSL nao esta pronto." -ForegroundColor Red
        Write-Host "Execute  .\scripts\ps\wsl-check.ps1  para ver os detalhes." -ForegroundColor Yellow
        exit 1
    }
    Ok "Ambiente WSL validado"
} else {
    Info "Nao e Windows — pulando wsl-check"
}

# ─── 1. Cluster k3d ───────────────────────────────────────────────────────────
Banner "1. Cluster k3d '$CLUSTER_NAME'"
$clusters = RunInWSL "k3d cluster list --no-headers 2>/dev/null" | ForEach-Object { ($_ -split '\s+')[0] }
if ($clusters -contains $CLUSTER_NAME) {
    Ok "Cluster '$CLUSTER_NAME' ja existe"

    # Garantir que o cluster esta rodando
    $running = RunInWSL "k3d cluster list --no-headers 2>/dev/null" | Where-Object { $_ -match "^$CLUSTER_NAME\s" }
    if ($running -and $running -match '\b[1-9]\d*/') {
        Info "Cluster ja esta em execucao"
    } else {
        Info "Iniciando cluster '$CLUSTER_NAME'..."
        RunInWSL "k3d cluster start $CLUSTER_NAME"
        Ok "Cluster iniciado"
    }
} else {
    Info "Criando cluster '$CLUSTER_NAME'..."
    RunInWSL "k3d cluster create $CLUSTER_NAME --port 8080:80@loadbalancer --port 8443:443@loadbalancer --k3s-arg '--disable=traefik@server:0'"
    Ok "Cluster criado"
}

$null = RunInWSL "kubectl config use-context $CLUSTER_CONTEXT 2>&1"
if ($LASTEXITCODE -ne 0) { Err "Falha ao definir contexto kubectl '$CLUSTER_CONTEXT'" }
Ok "Contexto kubectl: $CLUSTER_CONTEXT"

# Sincronizar kubeconfig com Windows (symlink permanente — idempotente)
Info "Sincronizando kubeconfig com Windows..."
RunInWSL "bash $WSL_REPO/scripts/sh/setup-kubeconfig-link.sh"
Ok "Kubeconfig sincronizado com Windows (~/.kube/config)"

# ─── 2. Nginx Ingress Controller ──────────────────────────────────────────────
Banner "2. Nginx Ingress Controller"
$ingressNs = RunInWSL "kubectl get namespace ingress-nginx --no-headers 2>/dev/null"
if ($ingressNs) {
    $dp = RunInWSL "kubectl get deployment ingress-nginx-controller -n ingress-nginx --no-headers 2>/dev/null"
    if ($dp -match '\s[1-9]\d*/') {
        Ok "Nginx ingress controller ja esta pronto"
    } else {
        Info "Nginx ingress existe mas nao esta pronto — aguardando..."
        RunInWSL "kubectl wait --namespace ingress-nginx --for=condition=ready pod --selector=app.kubernetes.io/component=controller --timeout=180s"
        Ok "Nginx ingress controller pronto"
    }
} else {
    Info "Instalando nginx ingress controller..."
    RunInWSL "kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.10.1/deploy/static/provider/cloud/deploy.yaml"
    Info "Aguardando nginx ingress controller..."
    RunInWSL "kubectl wait --namespace ingress-nginx --for=condition=ready pod --selector=app.kubernetes.io/component=controller --timeout=180s"
    Ok "Nginx ingress controller instalado e pronto"
}

# ─── 3. Build e import das imagens Docker ─────────────────────────────────────
if (-not $SkipBuild) {
    Banner "3. Imagens Docker"
    $images = @(
        @{ Name = 'precoapi:latest';   Dockerfile = "src\Services\PrecoAPI\Dockerfile" }
        @{ Name = 'produtoapi:latest'; Dockerfile = "src\Services\ProdutoAPI\Dockerfile" }
        @{ Name = 'mcpserver:latest';  Dockerfile = "src\Services\McpServer\Dockerfile" }
    )

    foreach ($img in $images) {
        $wslDockerfile = "$WSL_REPO/" + ($img.Dockerfile -replace '\\', '/')
        Info "Construindo $($img.Name)..."
        RunInWSL "docker build -f $wslDockerfile -t $($img.Name) $WSL_REPO"
        if ($LASTEXITCODE -ne 0) { Err "Falha ao construir $($img.Name)" }
        Ok "Imagem $($img.Name) construida"
    }

    Info "Importando imagens para o cluster k3d..."
    RunInWSL "k3d image import precoapi:latest produtoapi:latest mcpserver:latest --cluster $CLUSTER_NAME"
    if ($LASTEXITCODE -ne 0) { Err "Falha ao importar imagens para o cluster" }
    Ok "Imagens importadas para '$CLUSTER_NAME'"
} else {
    Banner "3. Imagens Docker"
    Info "[-SkipBuild] pulando build das imagens Docker"
}

# ─── 4. Namespace e Manifests ─────────────────────────────────────────────────
Banner "4. Namespace e Manifests Kubernetes"

RunInWSL "kubectl apply -f $WSL_REPO/k8s/namespace.yaml"
Ok "Namespace '$NAMESPACE'"

$manifests = @(
    'postgres-produto'
    'postgres-preco'
    'jaeger'
    'prometheus'
    'loki'
    'promtail'
    'grafana'
    'mcpserver'
    'precoapi'
    'produtoapi'
)
foreach ($m in $manifests) {
    $null = RunInWSL "kubectl apply -f $WSL_REPO/k8s/$m 2>&1"
    if ($LASTEXITCODE -ne 0) { Err "Falha ao aplicar manifests de '$m'" }
    Ok "Manifest $m aplicado"
}

if ($CaptureBody) {
    $patchJson = '{"data":{"Otel__CaptureBody":"true"}}'
    Info "Habilitando CaptureBody no OTEL..."
    RunInWSL "kubectl patch configmap precoapi-config   -n $NAMESPACE --type merge -p '$patchJson'"
    RunInWSL "kubectl patch configmap produtoapi-config -n $NAMESPACE --type merge -p '$patchJson'"
    Ok "CaptureBody habilitado"
}

# ─── 5. Aguardar rollouts ─────────────────────────────────────────────────────
Banner "5. Aguardando rollouts"

$rollouts = @(
    @{ Kind = 'statefulset'; Name = 'postgres-produto' }
    @{ Kind = 'statefulset'; Name = 'postgres-preco' }
    @{ Kind = 'deployment';  Name = 'jaeger' }
    @{ Kind = 'deployment';  Name = 'prometheus' }
    @{ Kind = 'deployment';  Name = 'loki' }
    @{ Kind = 'deployment';  Name = 'grafana' }
    @{ Kind = 'deployment';  Name = 'mcpserver' }
    @{ Kind = 'deployment';  Name = 'precoapi' }
    @{ Kind = 'deployment';  Name = 'produtoapi' }
)

foreach ($r in $rollouts) {
    Info "Aguardando $($r.Kind)/$($r.Name)..."
    RunInWSL "kubectl rollout status $($r.Kind)/$($r.Name) -n $NAMESPACE --timeout=180s"
    if ($LASTEXITCODE -ne 0) { Err "Rollout falhou para $($r.Kind)/$($r.Name)" }
    Ok "$($r.Kind)/$($r.Name) pronto"
}

# ─── 6. Health Check ──────────────────────────────────────────────────────────
if (-not $SkipHealthCheck) {
    Banner "6. Health Check"

    # Port-forwards
    Info "Iniciando port-forwards..."
    $pfMap = @(
        @{ Svc = 'precoapi';   Local = 5001; Remote = 80 }
        @{ Svc = 'produtoapi'; Local = 5002; Remote = 80 }
        @{ Svc = 'mcpserver';  Local = 4000; Remote = 4000 }
        @{ Svc = 'prometheus'; Local = 9090; Remote = 9090 }
        @{ Svc = 'grafana';    Local = 3000; Remote = 3000 }
        @{ Svc = 'jaeger';     Local = 16686; Remote = 16686 }
    )
    foreach ($pf in $pfMap) {
        $pfCmd = "kubectl port-forward -n $NAMESPACE svc/$($pf.Svc) $($pf.Local):$($pf.Remote)"
        $proc = Start-Process wsl.exe `
            -ArgumentList @('--', 'bash', '-lc', $pfCmd) `
            -PassThru -WindowStyle Hidden -ErrorAction SilentlyContinue
        if ($proc) { $script:PfProcs += $proc }
    }
    Start-Sleep -Seconds 6
    Ok "$($script:PfProcs.Count) port-forward(s) iniciados"

    # Funcao helper HTTP
    function Test-Http {
        param([string]$Label, [string]$Url, [int]$Expected = 200)
        try {
            $r = Invoke-WebRequest -Uri $Url -Method GET -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
            if ($r.StatusCode -eq $Expected) { Pass "$Label -> HTTP $($r.StatusCode)" }
            else                             { Fail "$Label -> esperado $Expected, obtido $($r.StatusCode)" }
        } catch {
            $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
            if ($code -eq $Expected) { Pass "$Label -> HTTP $code" }
            else                     { Fail "$Label -> sem resposta ($Url)" }
        }
    }

    function Test-HttpBody {
        param([string]$Label, [string]$Url, [string]$Pattern)
        try {
            $body = (Invoke-WebRequest -Uri $Url -Method GET -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop).Content
            if ($body -match $Pattern) { Pass $Label }
            else                       { Fail "$Label (padrao '$Pattern' nao encontrado)" }
        } catch {
            Fail "$Label (sem resposta de $Url)"
        }
    }

    # Pods e deployments
    Write-Host ""
    Write-Host "  --- Pods ---" -ForegroundColor DarkGray
    $appLabels = @(
        'precoapi','produtoapi','mcpserver',
        'postgres-produto','postgres-preco',
        'jaeger','prometheus','grafana','loki','promtail'
    )
    foreach ($app in $appLabels) {
        $rows = RunInWSL "kubectl get pods -n $NAMESPACE -l 'app=$app' --no-headers 2>/dev/null"
        if ($rows) {
            $cols   = ($rows -split '\s+')
            $status = $cols[2]; $ready = $cols[1]
            if ($status -eq 'Running') { Pass "Pod $app -> Running ($ready)" }
            else                       { Fail "Pod $app -> $status ($ready)" }
        } else {
            Fail "Pod $app -> nenhum pod encontrado"
        }
    }

    # Endpoints HTTP
    Write-Host ""
    Write-Host "  --- Endpoints HTTP ---" -ForegroundColor DarkGray
    Test-Http "PrecoAPI   /scalar/v1"         "http://localhost:5001/scalar/v1"
    Test-Http "PrecoAPI   /metrics"           "http://localhost:5001/metrics"
    Test-Http "ProdutoAPI /scalar/v1"         "http://localhost:5002/scalar/v1"
    Test-Http "ProdutoAPI /metrics"           "http://localhost:5002/metrics"
    Test-Http "McpServer  /health"            "http://localhost:4000/health"
    Test-Http "Prometheus /api/v1/status"     "http://localhost:9090/api/v1/status/config"
    Test-Http "Grafana    /api/health"        "http://localhost:3000/api/health"
    Test-Http "Jaeger     UI"                 "http://localhost:16686"

    # Conteudo
    Write-Host ""
    Write-Host "  --- Conteudo ---" -ForegroundColor DarkGray
    Test-HttpBody "PrecoAPI   /metrics 'http_server'"   "http://localhost:5001/metrics"       "http_server"
    Test-HttpBody "ProdutoAPI /metrics 'http_server'"   "http://localhost:5002/metrics"       "http_server"
    Test-HttpBody "McpServer  /health 'healthy'"        "http://localhost:4000/health"        "healthy"
    Test-HttpBody "Grafana    /api/health 'ok'"         "http://localhost:3000/api/health"    '"ok"'

    # MCP protocolo
    Write-Host ""
    Write-Host "  --- MCP Protocolo ---" -ForegroundColor DarkGray
    try {
        $body = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"up-k8s","version":"1.0"}}}'
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

    # Prometheus targets
    Write-Host ""
    Write-Host "  --- Prometheus ---" -ForegroundColor DarkGray
    try {
        $targets = Invoke-RestMethod -Uri "http://localhost:9090/api/v1/targets" -TimeoutSec 5 -ErrorAction Stop
        $upCount = ($targets.data.activeTargets | Where-Object { $_.health -eq 'up' } | Measure-Object).Count
        if ($upCount -ge 2) { Pass "Prometheus: $upCount target(s) UP" }
        else                { Fail "Prometheus: apenas $upCount target(s) UP (esperado >= 2)" }
    } catch {
        Fail "Prometheus: sem resposta em /api/v1/targets"
    }

    Stop-PF
} else {
    Banner "6. Health Check"
    Info "[-SkipHealthCheck] pulando verificacao de endpoints"
}

# ─── Resumo ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor White
Write-Host "║                   Resumo                     ║" -ForegroundColor White
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor White

if ($script:FAIL -eq 0) {
    Write-Host ""
    Write-Host "  Ambiente Kubernetes esta UP e saudavel!" -ForegroundColor Green
    Write-Host "  PASS: $($script:PASS)   FAIL: $($script:FAIL)" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "  PASS: $($script:PASS)   FAIL: $($script:FAIL)" -ForegroundColor Yellow
    Write-Host "  Alguns checks falharam. Verifique os itens [FAIL] acima." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "  Acesso aos servicos (port-forward manual: .\scripts\ps\port-forward.ps1):" -ForegroundColor White
Write-Host "    PrecoAPI   -> http://localhost:5001/scalar/v1"
Write-Host "    ProdutoAPI -> http://localhost:5002/scalar/v1"
Write-Host "    McpServer  -> http://localhost:4000"
Write-Host "    Jaeger     -> http://localhost:16686"
Write-Host "    Prometheus -> http://localhost:9090"
Write-Host "    Grafana    -> http://localhost:3000  (admin/admin)"
Write-Host ""

if ($script:FAIL -gt 0) { exit 1 }
