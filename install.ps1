#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repository = if ($env:DOCTOR_API_MCP_REPOSITORY) { $env:DOCTOR_API_MCP_REPOSITORY } else { 'https://github.com/alexssantos/doctor-api-mcp' }
$ref = if ($env:DOCTOR_API_MCP_REF) { $env:DOCTOR_API_MCP_REF } else { 'master' }
$release = if ($env:DOCTOR_API_MCP_RELEASE) { $env:DOCTOR_API_MCP_RELEASE } else { 'doctor-api-mcp' }
$namespace = if ($env:DOCTOR_API_MCP_NAMESPACE) { $env:DOCTOR_API_MCP_NAMESPACE } else { 'mcp-apis' }

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempDir = [IO.Path]::GetFullPath((Join-Path $tempRoot ("doctor-api-mcp-" + [guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $tempDir | Out-Null

try {
    $archive = Join-Path $tempDir 'source.zip'
    Write-Host "[1/3] Baixando doctor-api-mcp ($ref)..." -ForegroundColor Cyan
    Invoke-WebRequest -UseBasicParsing -Uri "$repository/archive/refs/heads/$ref.zip" -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath $tempDir

    $sourceDir = Get-ChildItem -LiteralPath $tempDir -Directory | Select-Object -First 1
    $chartDir = Join-Path $sourceDir.FullName 'infra/helm/doctor-api-mcp'
    if (-not (Test-Path -LiteralPath (Join-Path $chartDir 'Chart.yaml'))) {
        throw 'Chart Helm não encontrado no pacote baixado.'
    }

    $useNative = (Get-Command helm -ErrorAction SilentlyContinue) -and (Get-Command kubectl -ErrorAction SilentlyContinue)
    $useWsl = -not $useNative -and (Get-Command wsl.exe -ErrorAction SilentlyContinue)
    if (-not $useNative -and -not $useWsl) {
        throw "Instale helm e kubectl, ou execute em um Windows com WSL configurado."
    }

    if ($useWsl) {
        $chartDir = (& wsl.exe -- wslpath -a $chartDir).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Não foi possível converter o caminho do chart para o WSL.' }
        $helmCommand = 'wsl.exe'
        $helmPrefix = @('--', 'helm')
        $kubectlCommand = 'wsl.exe'
        $kubectlPrefix = @('--', 'kubectl')
        Write-Host 'Usando helm e kubectl dentro do WSL.' -ForegroundColor DarkGray
    } else {
        $helmCommand = 'helm'
        $helmPrefix = @()
        $kubectlCommand = 'kubectl'
        $kubectlPrefix = @()
    }

    Write-Host "[2/3] Instalando release '$release' no namespace '$namespace'..." -ForegroundColor Cyan
    & $helmCommand @helmPrefix upgrade --install $release $chartDir --namespace $namespace --create-namespace --wait --timeout 5m
    if ($LASTEXITCODE -ne 0) { throw 'A instalação Helm falhou.' }

    Write-Host '[3/3] Validando o rollout...' -ForegroundColor Cyan
    & $kubectlCommand @kubectlPrefix rollout status "deployment/$release" --namespace $namespace --timeout=180s
    if ($LASTEXITCODE -ne 0) { throw 'O rollout não ficou pronto no tempo esperado.' }

    Write-Host ''
    Write-Host 'doctor-api-mcp instalado.' -ForegroundColor Green
    Write-Host ''
    Write-Host 'Abra o acesso local:'
    if ($useWsl) {
        Write-Host "  wsl kubectl port-forward service/$release 4000:4000 -n $namespace"
    } else {
        Write-Host "  kubectl port-forward service/$release 4000:4000 -n $namespace"
    }
    Write-Host ''
    Write-Host 'Dashboard: http://localhost:4000/dashboard'
    Write-Host 'MCP:       http://localhost:4000/'
}
finally {
    $resolvedTempDir = [IO.Path]::GetFullPath($tempDir)
    if ($resolvedTempDir.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedTempDir).StartsWith('doctor-api-mcp-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTempDir -Recurse -Force -ErrorAction SilentlyContinue
    } else {
        Write-Warning "Diretório temporário inesperado; limpeza ignorada: $resolvedTempDir"
    }
}
