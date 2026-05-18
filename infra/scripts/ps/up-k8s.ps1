# up-k8s.ps1 — Valida e levanta o ambiente Kubernetes (k3d) completo
# Cada etapa verifica o estado atual antes de agir: ja existindo, pula.
# Ao final executa health-check completo com port-forwards.
#
# Uso:
#   .\scripts\ps\up-k8s.ps1                   # deploy via Helm (padrao)
#   .\scripts\ps\up-k8s.ps1 -K8s             # deploy via kubectl raw manifests
#   .\scripts\ps\up-k8s.ps1 -Build            # executa build das imagens Docker
#   .\scripts\ps\up-k8s.ps1 -SkipHealthCheck  # pula verificacao final
#   .\scripts\ps\up-k8s.ps1 -CaptureBody      # habilita captura de body no OTEL
#Requires -Version 5.1
param(
    [switch]$K8s,
    [switch]$Build,
    [switch]$CaptureBody,
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = 'Stop'

$NAMESPACE       = 'mcp-apis'
$CLUSTER_NAME    = 'mcp-apis'
$CLUSTER_CONTEXT = "k3d-$CLUSTER_NAME"
$REPO_ROOT       = (Resolve-Path "$PSScriptRoot\..\..\..").Path
$driveLetter     = $REPO_ROOT.Substring(0,1).ToLower()
$WSL_REPO        = "/mnt/$driveLetter" + ($REPO_ROOT.Substring(2) -replace '\\', '/')

$script:PASS     = 0
$script:FAIL     = 0

function RunInWSL([string]$Cmd) { wsl.exe -- bash -lc $Cmd }
function Banner($msg) { Write-Host ""; Write-Host "══► $msg" -ForegroundColor Cyan }
function Ok($msg)     { Write-Host "    [OK]   $msg" -ForegroundColor Green;  $script:PASS++ }
function Warn($msg)   { Write-Host "    [WARN] $msg" -ForegroundColor Yellow }
function Info($msg)   { Write-Host "    ...    $msg" -ForegroundColor DarkGray }
function Err($msg)    { Write-Host ""; Write-Host "[ERRO] $msg`n" -ForegroundColor Red; exit 1 }
function Pass($msg)   { Write-Host "    [PASS] $msg" -ForegroundColor Green;  $script:PASS++ }
function Fail($msg)   { Write-Host "    [FAIL] $msg" -ForegroundColor Red;    $script:FAIL++ }

Write-Host ""
Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor White
Write-Host "║      mcp-apis  --  up-k8s                    ║" -ForegroundColor White
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor White

$deployMode = if ($K8s) { 'k8s' } else { 'helm' }
Write-Host "  Modo de deploy: " -NoNewline -ForegroundColor White
Write-Host $deployMode.ToUpper() -ForegroundColor $(if ($K8s) { 'Yellow' } else { 'Cyan' })

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
if ($Build) {
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
    Info "[padrao] pulando build das imagens Docker — use -Build para construir"
}

# ─── 4. Namespace e Deploy ────────────────────────────────────────────────────
Banner "4. Namespace e Deploy ($deployMode)"

RunInWSL "kubectl apply -f $WSL_REPO/infra/k8s/namespace.yaml"
Ok "Namespace '$NAMESPACE'"

if ($K8s) {
    # ── Raw kubectl manifests ──────────────────────────────────────────────────
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
        $null = RunInWSL "kubectl apply -f $WSL_REPO/infra/k8s/$m 2>&1"
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
} else {
    # ── Helm ───────────────────────────────────────────────────────────────────
    Info "Adicionando repositorio Bitnami..."
    RunInWSL "helm repo add bitnami https://charts.bitnami.com/bitnami 2>/dev/null || true"
    RunInWSL "helm repo update"
    Ok "Repositorio Bitnami atualizado"

    Info "Instalando PostgreSQL para ProdutoDB..."
    RunInWSL "helm upgrade --install postgres-produto bitnami/postgresql --namespace $NAMESPACE --set auth.username=postgres --set auth.password=postgres --set auth.database=produto_db --wait --timeout 120s"
    if ($LASTEXITCODE -ne 0) { Err "Falha ao instalar postgres-produto via Helm" }
    Ok "PostgreSQL produto instalado"

    Info "Instalando PostgreSQL para PrecoDB..."
    RunInWSL "helm upgrade --install postgres-preco bitnami/postgresql --namespace $NAMESPACE --set auth.username=postgres --set auth.password=postgres --set auth.database=preco_db --wait --timeout 120s"
    if ($LASTEXITCODE -ne 0) { Err "Falha ao instalar postgres-preco via Helm" }
    Ok "PostgreSQL preco instalado"

    # Observability stack permanece via kubectl (sem Helm chart dedicado)
    $obsManifests = @('jaeger', 'prometheus', 'loki', 'promtail', 'grafana')
    foreach ($m in $obsManifests) {
        $null = RunInWSL "kubectl apply -f $WSL_REPO/infra/k8s/$m 2>&1"
        if ($LASTEXITCODE -ne 0) { Err "Falha ao aplicar manifests de '$m'" }
        Ok "Manifest $m aplicado"
    }

    $captureBodyFlag = if ($CaptureBody) { '--set otel.captureBody=true' } else { '' }

    Info "Instalando PrecoAPI via Helm..."
    RunInWSL "helm upgrade --install precoapi $WSL_REPO/infra/helm/precoapi --namespace $NAMESPACE --set db.host=postgres-preco-postgresql $captureBodyFlag --wait --timeout 120s"
    if ($LASTEXITCODE -ne 0) { Err "Falha ao instalar precoapi via Helm" }
    Ok "PrecoAPI instalada"

    Info "Instalando ProdutoAPI via Helm..."
    RunInWSL "helm upgrade --install produtoapi $WSL_REPO/infra/helm/produtoapi --namespace $NAMESPACE --set db.host=postgres-produto-postgresql $captureBodyFlag --wait --timeout 120s"
    if ($LASTEXITCODE -ne 0) { Err "Falha ao instalar produtoapi via Helm" }
    Ok "ProdutoAPI instalada"

    Info "Instalando MCP Server via Helm..."
    RunInWSL "helm upgrade --install mcpserver $WSL_REPO/infra/helm/mcpserver --namespace $NAMESPACE --wait --timeout 120s"
    if ($LASTEXITCODE -ne 0) { Err "Falha ao instalar mcpserver via Helm" }
    Ok "MCP Server instalado"
}

# ─── 5. Aguardar rollouts ─────────────────────────────────────────────────────
Banner "5. Aguardando rollouts"

if ($K8s) {
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
} else {
    $rollouts = @(
        @{ Kind = 'statefulset'; Name = 'postgres-produto-postgresql' }
        @{ Kind = 'statefulset'; Name = 'postgres-preco-postgresql' }
        @{ Kind = 'deployment';  Name = 'jaeger' }
        @{ Kind = 'deployment';  Name = 'prometheus' }
        @{ Kind = 'deployment';  Name = 'loki' }
        @{ Kind = 'deployment';  Name = 'grafana' }
        @{ Kind = 'deployment';  Name = 'mcpserver' }
        @{ Kind = 'deployment';  Name = 'precoapi' }
        @{ Kind = 'deployment';  Name = 'produtoapi' }
    )
}

foreach ($r in $rollouts) {
    Info "Aguardando $($r.Kind)/$($r.Name)..."
    RunInWSL "kubectl rollout status $($r.Kind)/$($r.Name) -n $NAMESPACE --timeout=180s"
    if ($LASTEXITCODE -ne 0) { Err "Rollout falhou para $($r.Kind)/$($r.Name)" }
    Ok "$($r.Kind)/$($r.Name) pronto"
}

# ─── 6. Health Check ──────────────────────────────────────────────────────────
if (-not $SkipHealthCheck) {
    Banner "6. Health Check"
    Info "Executando health check (bash)..."

    # Delega para o bash script que ja gerencia os port-forwards corretamente
    $hcOutput = RunInWSL "bash $WSL_REPO/infra/scripts/sh/k8s/health-check.sh 2>&1"
    $hcOutput | ForEach-Object { Write-Host "  $_" }

    # Extrai contadores da linha HEALTH_SUMMARY gerada pelo bash script
    $summaryLine = $hcOutput | Where-Object { $_ -match '^HEALTH_SUMMARY:' }
    if ($summaryLine -match 'pass=(\d+):fail=(\d+)') {
        $script:PASS += [int]$Matches[1]
        $script:FAIL += [int]$Matches[2]
    }
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
