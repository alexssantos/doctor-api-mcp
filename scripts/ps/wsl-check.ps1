# wsl-check.ps1 — Valida que o ambiente WSL e as ferramentas estao prontos
# para executar os scripts PS e SH do projeto mcp-apis.
# Usage: .\scripts\ps\wsl-check.ps1
#        .\scripts\ps\wsl-check.ps1 -Quiet   (apenas exit code, sem output)
#Requires -Version 5.1
param([switch]$Quiet)

$script:PASS = 0
$script:FAIL = 0
$script:WARN = 0

function Pass($msg) { if (-not $Quiet) { Write-Host "  [OK]   $msg" -ForegroundColor Green  }; $script:PASS++ }
function Fail($msg) { if (-not $Quiet) { Write-Host "  [FAIL] $msg" -ForegroundColor Red    }; $script:FAIL++ }
function Warn($msg) { if (-not $Quiet) { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }; $script:WARN++ }
function Section($t){ if (-not $Quiet) { Write-Host ""; Write-Host "=== $t ===" -ForegroundColor Cyan } }

function Invoke-Wsl([string]$cmd) {
    $out = wsl -- bash -c $cmd 2>&1
    return [PSCustomObject]@{ Output = ($out -join "`n").Trim(); Ok = ($LASTEXITCODE -eq 0) }
}

if (-not $Quiet) {
    Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor White
    Write-Host "║     mcp-apis — WSL Environment Check         ║" -ForegroundColor White
    Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor White
}

# ─── 1. WSL disponivel ────────────────────────────────────────────────────────
Section "1. WSL"
if (Get-Command wsl.exe -ErrorAction SilentlyContinue) {
    $wslVerLine = wsl --version 2>&1 | Select-String 'Versao do WSL|WSL version' | Select-Object -First 1
    $wslVer = if ($wslVerLine) { $wslVerLine.ToString().Trim() } else { (wsl --version 2>&1 | Select-Object -First 1).ToString().Trim() }
    Pass "wsl.exe disponivel ($wslVer)"
} else {
    Fail "wsl.exe nao encontrado — instale o WSL: wsl --install"
    if (-not $Quiet) { Write-Host ""; Write-Host "WSL nao encontrado. Nao e possivel continuar." -ForegroundColor Red }
    exit 1
}

# Distro padrao respondendo
$pingResult = Invoke-Wsl "echo ok"
if ($pingResult.Ok -and $pingResult.Output -eq 'ok') {
    $distroLine = wsl --list --running 2>&1 | Where-Object { $_ -match '\S' } | Select-Object -Skip 1 -First 1
    $distro = if ($distroLine) { $distroLine.ToString().Trim() } else { 'default' }
    Pass "Distro WSL respondendo ($distro)"
} else {
    Fail "WSL nao respondeu ao ping. Inicie o WSL: wsl"
    exit 1
}

# ─── 2. PATH no WSL ───────────────────────────────────────────────────────────
Section "2. PATH no WSL"
$pathResult = Invoke-Wsl 'echo $PATH'
if ($pathResult.Output -match '\.local/bin') {
    Pass "~/.local/bin esta no PATH do WSL"
} else {
    Warn "~/.local/bin NAO esta no PATH do WSL"
    if (-not $Quiet) {
        Write-Host "        Adicione ao ~/.bashrc:" -ForegroundColor DarkGray
        Write-Host '        export PATH="$HOME/.local/bin:$PATH"' -ForegroundColor DarkGray
    }
}

# ─── 3. Ferramentas no WSL ────────────────────────────────────────────────────
Section "3. Ferramentas no WSL"
$wslTools = @{
    'docker'  = 'docker --version'
    'kubectl' = 'kubectl version --client --short 2>/dev/null || kubectl version --client 2>/dev/null | head -1'
    'k3d'     = 'k3d version 2>/dev/null | head -1'
    'helm'    = 'helm version --short 2>/dev/null'
    'curl'    = 'curl --version 2>/dev/null | head -1'
    'python3' = 'python3 --version 2>/dev/null'
}

foreach ($entry in $wslTools.GetEnumerator()) {
    $res = Invoke-Wsl "export PATH=`"`$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin`"; $($entry.Value)"
    if ($res.Ok -and $res.Output) {
        $ver = $res.Output.Split("`n")[0].Trim()
        Pass "$($entry.Key) → $ver"
    } else {
        if ($entry.Key -eq 'python3') {
            Warn "$($entry.Key) nao encontrado (necessario para validate-phase3)"
        } else {
            Fail "$($entry.Key) nao encontrado no WSL. Execute: bash scripts/sh/install-tools-wsl.sh"
        }
    }
}

# ─── 4. Docker daemon ─────────────────────────────────────────────────────────
Section "4. Docker daemon"
$dockerPing = Invoke-Wsl "export PATH=`"`$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin`"; docker info --format '{{.ServerVersion}}' 2>/dev/null"
if ($dockerPing.Ok -and $dockerPing.Output) {
    Pass "Docker daemon acessivel no WSL (engine $($dockerPing.Output))"
} else {
    $dockerBin = Invoke-Wsl "which docker 2>/dev/null"
    if ($dockerBin.Ok -and $dockerBin.Output) {
        Warn "Docker instalado no WSL mas daemon nao esta rodando. Inicie com: sudo service docker start"
    } else {
        Fail "Docker NAO instalado no WSL. Verifique o Docker Desktop (Settings > Resources > WSL Integration)"
    }
}

# Verificar Docker nativo no Windows (usado pelos scripts PS)
$dockerWinPaths = @(
    'docker',
    'C:\Program Files\Docker\Docker\resources\bin\docker.exe',
    "$env:LOCALAPPDATA\Programs\Docker\Docker\resources\bin\docker.exe"
)
$dockerWin = $dockerWinPaths | Where-Object { Get-Command $_ -ErrorAction SilentlyContinue } | Select-Object -First 1
if ($dockerWin) {
    $winDockerVer = & docker version --format '{{.Server.Version}}' 2>$null
    if ($LASTEXITCODE -eq 0) { Pass "Docker daemon acessivel no Windows (engine $winDockerVer)" }
    else                     { Warn "Docker disponivel no Windows mas daemon nao responde" }
} else {
    Warn "docker nao encontrado no Windows PATH (scripts PS usam docker diretamente)"
}

# ─── 5. Ferramentas nativas Windows (para scripts PS) ─────────────────────────
Section "5. Ferramentas nativas Windows (scripts PS)"
$winTools = @('kubectl', 'k3d', 'helm')
foreach ($cmd in $winTools) {
    if (Get-Command $cmd -ErrorAction SilentlyContinue) {
        $ver = & $cmd version --short 2>$null
        if (-not $ver) { $ver = & $cmd --version 2>$null }
        if (-not $ver) { $ver = 'ok' }
        Pass "$cmd disponivel no Windows"
    } else {
        Warn "$cmd nao encontrado no Windows PATH — scripts PS/$(${cmd}).ps1 precisam dele"
    }
}

# ─── 6. Repo acessivel no WSL ─────────────────────────────────────────────────
Section "6. Repositorio"
$repoPath   = "c:\dev\repos\projetos\mcp-apis"
$wslRepoPath = "/mnt/c/dev/repos/projetos/mcp-apis"
$repoCheck  = Invoke-Wsl "test -d '$wslRepoPath' && echo ok"
if ($repoCheck.Output -eq 'ok') {
    Pass "Repo acessivel no WSL em $wslRepoPath"
} else {
    Fail "Repo NAO encontrado em $wslRepoPath (caminho WSL esperado)"
}

if (Test-Path $repoPath) { Pass "Repo acessivel no Windows em $repoPath" }
else                     { Fail "Repo NAO encontrado em $repoPath" }

# ─── 7. k3d cluster ───────────────────────────────────────────────────────────
Section "7. Cluster k3d (opcional)"
$clusterCheck = Invoke-Wsl "export PATH=`"`$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin`"; k3d cluster list --no-headers 2>/dev/null"
if ($clusterCheck.Ok -and $clusterCheck.Output -match 'mcp-apis') {
    Pass "Cluster 'mcp-apis' existe no WSL"
} else {
    Warn "Cluster 'mcp-apis' nao existe ainda — execute deploy-k8s.ps1 ou deploy-helm.ps1"
}

# ─── Resumo ───────────────────────────────────────────────────────────────────
if (-not $Quiet) {
    Write-Host ""
    Write-Host "══════════════════════════════════════════════" -ForegroundColor White
    $total = $script:PASS + $script:FAIL + $script:WARN
    Write-Host "  " -NoNewline
    Write-Host "$($script:PASS) ok" -ForegroundColor Green -NoNewline
    Write-Host "  /  " -NoNewline
    Write-Host "$($script:WARN) aviso(s)" -ForegroundColor Yellow -NoNewline
    Write-Host "  /  " -NoNewline
    Write-Host "$($script:FAIL) falhou" -ForegroundColor Red -NoNewline
    Write-Host "  (total: $total)"
    Write-Host "══════════════════════════════════════════════" -ForegroundColor White

    if ($script:FAIL -gt 0) {
        Write-Host ""
        Write-Host "Ambiente nao esta pronto. Corrija os itens [FAIL] antes de prosseguir." -ForegroundColor Red
    } elseif ($script:WARN -gt 0) {
        Write-Host ""
        Write-Host "Ambiente pronto com avisos. Scripts PS funcionarao, mas verifique os [WARN]." -ForegroundColor Yellow
    } else {
        Write-Host ""
        Write-Host "Ambiente pronto." -ForegroundColor Green
    }
    Write-Host ""
}

if ($script:FAIL -gt 0) { exit 1 }
exit 0
