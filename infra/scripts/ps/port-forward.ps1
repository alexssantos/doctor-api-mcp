# port-forward.ps1 — Redireciona todos os servicos mcp-apis para localhost.
# Pressione Ctrl+C para encerrar todos os port-forwards.
#
# Uso:
#   .\infra\scripts\ps\port-forward.ps1
#
# Alternativa sem port-forward (via Nginx Ingress):
#   .\infra\scripts\ps\setup-hosts.ps1   (executar uma vez, requer admin)
#   Depois acesse: http://precoapi.local:8080 etc.
#Requires -Version 5.1

$REPO_ROOT    = (Resolve-Path "$PSScriptRoot\..\..\..").Path
$driveLetter  = $REPO_ROOT.Substring(0,1).ToLower()
$WSL_REPO     = "/mnt/$driveLetter" + ($REPO_ROOT.Substring(2) -replace '\\', '/')

try {
    wsl.exe -- bash -lc "bash $WSL_REPO/infra/scripts/sh/port-forward.sh"
} finally {
    wsl.exe -- bash -lc "pkill -f 'kubectl port-forward' 2>/dev/null || true" | Out-Null
}
