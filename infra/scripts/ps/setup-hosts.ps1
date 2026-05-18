# setup-hosts.ps1 — Adiciona entradas no hosts do Windows para acesso via Nginx Ingress.
#
# Permite acessar os servicos em http://<nome>.local:8080 sem port-forward.
# O k3d ja mapeia localhost:8080 -> Nginx Ingress -> servico correto por hostname.
#
# Uso (requer PowerShell como Administrador):
#   .\infra\scripts\ps\setup-hosts.ps1
#
# Utilize o comando flushdns para limpar o cache de DNS do Windows, caso necessario:
#   ipconfig /flushdns
#
# URLs disponiveis apos executar este script:
#   http://precoapi.local:8080/scalar/v1
#   http://produtoapi.local:8080/scalar/v1
#   http://mcpserver.local:8080/health
#   http://jaeger.local:8080
#   http://prometheus.local:8080
#   http://grafana.local:8080   (admin/admin)
#Requires -Version 5.1

$HOSTS_FILE = "C:\Windows\System32\drivers\etc\hosts"
$MARKER     = "# mcp-apis k3d ingress"

$entries = @(
    "127.0.0.1  precoapi.local    $MARKER"
    "127.0.0.1  produtoapi.local  $MARKER"
    "127.0.0.1  mcpserver.local   $MARKER"
    "127.0.0.1  jaeger.local      $MARKER"
    "127.0.0.1  prometheus.local  $MARKER"
    "127.0.0.1  grafana.local     $MARKER"
)

# Verificar permissao de escrita
try {
    $null = [System.IO.File]::OpenWrite($HOSTS_FILE)
} catch {
    Write-Host ""
    Write-Host "[ERRO] Sem permissao para editar o hosts. Execute como Administrador." -ForegroundColor Red
    Write-Host "       Clique com botao direito no PowerShell > Executar como Administrador" -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

$current = Get-Content $HOSTS_FILE -Raw

# Remover entradas antigas do mcp-apis (para re-adicionar limpas)
$lines = (Get-Content $HOSTS_FILE) | Where-Object { $_ -notmatch [regex]::Escape($MARKER) }

# Adicionar novas entradas
$lines += ""
$lines += $entries

Set-Content -Path $HOSTS_FILE -Value $lines -Encoding ASCII

Write-Host ""
Write-Host "[OK]  Hosts atualizados. Acesse via Nginx Ingress (porta 8080):" -ForegroundColor Green
Write-Host ""
Write-Host "   http://precoapi.local:8080/scalar/v1" -ForegroundColor Cyan
Write-Host "   http://produtoapi.local:8080/scalar/v1" -ForegroundColor Cyan
Write-Host "   http://mcpserver.local:8080/health" -ForegroundColor Cyan
Write-Host "   http://jaeger.local:8080" -ForegroundColor Cyan
Write-Host "   http://prometheus.local:8080" -ForegroundColor Cyan
Write-Host "   http://grafana.local:8080   (admin/admin)" -ForegroundColor Cyan
Write-Host ""
Write-Host "   Nenhum port-forward necessario." -ForegroundColor DarkGray
Write-Host ""
