# refresh-mcpserver.ps1 — Rebuild e redeploy do McpServer no cluster k3d.
#
# Uso:
#   .\infra\scripts\ps\refresh-mcpserver.ps1
#Requires -Version 5.1

$ErrorActionPreference = 'Stop'

$NAMESPACE    = 'mcp-apis'
$CLUSTER_NAME = 'mcp-apis'
$IMAGE        = 'mcpserver:latest'
$DOCKERFILE   = 'src\Services\McpServer\Dockerfile'
$REPO_ROOT    = (Resolve-Path "$PSScriptRoot\..\..\..").Path
$driveLetter  = $REPO_ROOT.Substring(0,1).ToLower()
$WSL_REPO     = "/mnt/$driveLetter" + ($REPO_ROOT.Substring(2) -replace '\\', '/')

function RunInWSL([string]$Cmd) { wsl.exe -- bash -lc $Cmd }
function Banner($msg) { Write-Host ""; Write-Host "══► $msg" -ForegroundColor Cyan }
function Ok($msg)     { Write-Host "    [OK]   $msg" -ForegroundColor Green }
function Info($msg)   { Write-Host "    ...    $msg" -ForegroundColor DarkGray }
function Err($msg)    { Write-Host ""; Write-Host "[ERRO] $msg`n" -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor White
Write-Host "║     mcp-apis  --  refresh-mcpserver          ║" -ForegroundColor White
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor White

# ─── 1. Build ─────────────────────────────────────────────────────────────────
Banner "1. Build da imagem Docker"
$wslDockerfile = "$WSL_REPO/" + ($DOCKERFILE -replace '\\', '/')
Info "Construindo $IMAGE..."
RunInWSL "docker build -f $wslDockerfile -t $IMAGE $WSL_REPO"
if ($LASTEXITCODE -ne 0) { Err "Falha no build de $IMAGE" }
Ok "Imagem $IMAGE construida"

# ─── 2. Import no cluster k3d ─────────────────────────────────────────────────
Banner "2. Import no cluster k3d '$CLUSTER_NAME'"
Info "Importando $IMAGE..."
RunInWSL "k3d image import $IMAGE --cluster $CLUSTER_NAME"
if ($LASTEXITCODE -ne 0) { Err "Falha ao importar $IMAGE no cluster" }
Ok "Imagem importada"

# ─── 3. Restart do deployment ─────────────────────────────────────────────────
Banner "3. Restart do deployment"
Info "Reiniciando deployment/mcpserver..."
RunInWSL "kubectl rollout restart deployment/mcpserver -n $NAMESPACE"
if ($LASTEXITCODE -ne 0) { Err "Falha ao reiniciar deployment/mcpserver" }

Info "Aguardando rollout..."
RunInWSL "kubectl rollout status deployment/mcpserver -n $NAMESPACE --timeout=120s"
if ($LASTEXITCODE -ne 0) { Err "Rollout nao concluido dentro do timeout" }
Ok "mcpserver rodando com a nova imagem"

Write-Host ""
Write-Host "  McpServer -> http://localhost:4000" -ForegroundColor White
Write-Host ""
