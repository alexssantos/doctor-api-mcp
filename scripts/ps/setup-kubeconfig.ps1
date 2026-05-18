# setup-kubeconfig.ps1 — Configura symlink do kubeconfig WSL -> Windows (executar uma vez)
#
# Cria um symlink permanente de ~/.kube/config (WSL) apontando para
# C:\Users\<user>\.kube\config, tornando o kubeconfig do k3d imediatamente
# visivel para Lens e outras ferramentas Windows.
#
# Uso: .\scripts\ps\setup-kubeconfig.ps1
#Requires -Version 5.1

$ErrorActionPreference = 'Stop'
$REPO_ROOT    = (Resolve-Path "$PSScriptRoot\..\..").Path
$driveLetter  = $REPO_ROOT.Substring(0, 1).ToLower()
$WSL_REPO     = "/mnt/$driveLetter" + ($REPO_ROOT.Substring(2) -replace '\\', '/')

function RunInWSL([string]$Cmd) { wsl.exe -- bash -lc $Cmd }

Write-Host ""
Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor White
Write-Host "║   mcp-apis  --  setup-kubeconfig             ║" -ForegroundColor White
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor White
Write-Host ""

RunInWSL "bash $WSL_REPO/scripts/sh/setup-kubeconfig-link.sh"

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "  [OK] Kubeconfig sincronizado com Windows (~/.kube/config)" -ForegroundColor Green
    Write-Host "       Lens e outras ferramentas Windows ja podem usar o cluster." -ForegroundColor DarkGray
} else {
    Write-Host ""
    Write-Host "  [ERRO] Falha ao configurar symlink do kubeconfig." -ForegroundColor Red
    exit 1
}
Write-Host ""
