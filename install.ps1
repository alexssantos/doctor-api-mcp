#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Get-EnvironmentValue([string]$Name, [string]$DefaultValue) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) { return $DefaultValue }
    return $value
}

function Get-EnvironmentBoolean([string]$Name, [bool]$DefaultValue) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) { return $DefaultValue }
    $parsed = $false
    if (-not [bool]::TryParse($value, [ref]$parsed)) {
        throw "$Name deve ser true ou false."
    }
    return $parsed
}

$repository = Get-EnvironmentValue 'DOCTOR_API_MCP_REPOSITORY' 'https://github.com/alexssantos/doctor-api-mcp'
$ref = Get-EnvironmentValue 'DOCTOR_API_MCP_REF' 'master'
$release = Get-EnvironmentValue 'DOCTOR_API_MCP_RELEASE' 'doctor-api-mcp'
$namespace = Get-EnvironmentValue 'DOCTOR_API_MCP_NAMESPACE' 'mcp-apis'
$mode = Get-EnvironmentValue 'DOCTOR_API_MCP_MODE' 'cluster'

$profile = switch ($mode) {
    'cluster' {
        @{ Scope = 'Cluster'; Discovery = $true; State = 'ConfigMap'; Volumes = $true; Events = $true; Replicas = 2; Pdb = $true }
    }
    'namespace' {
        @{ Scope = 'Namespace'; Discovery = $true; State = 'ConfigMap'; Volumes = $true; Events = $true; Replicas = 2; Pdb = $true }
    }
    'no-volumes' {
        @{ Scope = 'Cluster'; Discovery = $true; State = 'ConfigMap'; Volumes = $false; Events = $true; Replicas = 2; Pdb = $true }
    }
    'no-service-discovery' {
        @{ Scope = 'Namespace'; Discovery = $false; State = 'ConfigMap'; Volumes = $true; Events = $true; Replicas = 2; Pdb = $true }
    }
    'restricted' {
        @{ Scope = 'None'; Discovery = $false; State = 'Memory'; Volumes = $false; Events = $false; Replicas = 1; Pdb = $false }
    }
    default {
        throw 'DOCTOR_API_MCP_MODE deve ser cluster, namespace, no-volumes, no-service-discovery ou restricted.'
    }
}

$accessScope = Get-EnvironmentValue 'DOCTOR_API_MCP_ACCESS_SCOPE' $profile.Scope
$serviceDiscovery = Get-EnvironmentBoolean 'DOCTOR_API_MCP_SERVICE_DISCOVERY' $profile.Discovery
$stateStorage = Get-EnvironmentValue 'DOCTOR_API_MCP_STATE_STORAGE' $profile.State
$allowVolumes = Get-EnvironmentBoolean 'DOCTOR_API_MCP_ALLOW_VOLUMES' $profile.Volumes
$deploymentEvents = Get-EnvironmentBoolean 'DOCTOR_API_MCP_DEPLOYMENT_EVENTS' $profile.Events
$replicas = [int](Get-EnvironmentValue 'DOCTOR_API_MCP_REPLICAS' ([string]$profile.Replicas))
$pdb = Get-EnvironmentBoolean 'DOCTOR_API_MCP_PDB' $profile.Pdb
$runPreflight = Get-EnvironmentBoolean 'DOCTOR_API_MCP_PREFLIGHT' $true
$serviceName = Get-EnvironmentValue 'DOCTOR_API_MCP_SERVICE_NAME' ''
$serviceUrl = Get-EnvironmentValue 'DOCTOR_API_MCP_SERVICE_URL' ''

if (-not $serviceDiscovery) {
    if ([string]::IsNullOrWhiteSpace($serviceName) -or [string]::IsNullOrWhiteSpace($serviceUrl)) {
        throw 'Modos sem service discovery exigem DOCTOR_API_MCP_SERVICE_NAME e DOCTOR_API_MCP_SERVICE_URL.'
    }
    if ($serviceName -notmatch '^[A-Za-z0-9_]+$') {
        throw 'DOCTOR_API_MCP_SERVICE_NAME aceita apenas letras, números e underscore.'
    }
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempDir = [IO.Path]::GetFullPath((Join-Path $tempRoot ("doctor-api-mcp-" + [guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $tempDir | Out-Null

try {
    $archive = Join-Path $tempDir 'source.zip'
    Write-Host "[1/5] Baixando doctor-api-mcp ($ref)..." -ForegroundColor Cyan
    Invoke-WebRequest -UseBasicParsing -Uri "$repository/archive/refs/heads/$ref.zip" -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath $tempDir

    $sourceDir = Get-ChildItem -LiteralPath $tempDir -Directory | Select-Object -First 1
    $chartDir = Join-Path $sourceDir.FullName 'infra/helm/doctor-api-mcp'
    $preflightPs = Join-Path $sourceDir.FullName 'infra/scripts/ps/validate-install-requirements.ps1'
    $preflightSh = Join-Path $sourceDir.FullName 'infra/scripts/sh/validate-install-requirements.sh'
    if (-not (Test-Path -LiteralPath (Join-Path $chartDir 'Chart.yaml'))) {
        throw 'Chart Helm não encontrado no pacote baixado.'
    }

    $useNative = (Get-Command helm -ErrorAction SilentlyContinue) -and (Get-Command kubectl -ErrorAction SilentlyContinue)
    $useWsl = -not $useNative -and (Get-Command wsl.exe -ErrorAction SilentlyContinue)
    if (-not $useNative -and -not $useWsl) {
        throw 'Instale helm e kubectl, ou execute em um Windows com WSL configurado.'
    }

    if ($useWsl) {
        $chartDir = (& wsl.exe -- wslpath -a $chartDir).Trim()
        $preflightSh = (& wsl.exe -- wslpath -a $preflightSh).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Não foi possível converter os caminhos para o WSL.' }
        Write-Host 'Usando helm e kubectl dentro do WSL.' -ForegroundColor DarkGray
    }

    function Invoke-ClusterTool {
        param(
            [Parameter(Mandatory)][string]$Tool,
            [Parameter(Mandatory)][string[]]$Arguments,
            [Parameter(Mandatory)][string]$FailureMessage
        )

        if ($useWsl) {
            $output = & wsl.exe -- bash -lc `
                'export PATH="$HOME/.local/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"; exec "$@"' `
                doctor-api-mcp-installer $Tool @Arguments
        }
        else {
            $output = & $Tool @Arguments
        }
        if ($LASTEXITCODE -ne 0) { throw $FailureMessage }
        return $output
    }

    if ($runPreflight) {
        Write-Host "[2/5] Validando permissões mínimas do instalador (modo: $mode)..." -ForegroundColor Cyan
        if ($useWsl) {
            Invoke-ClusterTool -Tool 'bash' -Arguments @(
                $preflightSh,
                '--phase', 'installer',
                '--namespace', $namespace,
                '--release', $release,
                '--scope', $accessScope,
                '--service-discovery', $serviceDiscovery.ToString().ToLowerInvariant(),
                '--state-storage', $stateStorage,
                '--deployment-events', $deploymentEvents.ToString().ToLowerInvariant(),
                '--pdb', $pdb.ToString().ToLowerInvariant()
            ) -FailureMessage 'A validação de permissões do instalador falhou.'
        }
        else {
            & $preflightPs -Phase installer -Namespace $namespace -Release $release `
                -Scope $accessScope -ServiceDiscovery $serviceDiscovery `
                -StateStorage $stateStorage -DeploymentEvents $deploymentEvents -Pdb $pdb
        }
    }
    else {
        Write-Host '[2/5] Preflight desabilitado por DOCTOR_API_MCP_PREFLIGHT=false.' -ForegroundColor DarkYellow
    }

    $helmArguments = @(
        'upgrade', '--install', $release, $chartDir,
        '--namespace', $namespace,
        '--create-namespace',
        '--wait',
        '--timeout', '5m',
        '--set-string', "clusterAccess.scope=$accessScope",
        '--set', "clusterAccess.serviceDiscovery=$($serviceDiscovery.ToString().ToLowerInvariant())",
        '--set-string', "clusterAccess.stateStorage=$stateStorage",
        '--set', "clusterAccess.allowVolumes=$($allowVolumes.ToString().ToLowerInvariant())",
        '--set', "observability.enableDeploymentEvents=$($deploymentEvents.ToString().ToLowerInvariant())",
        '--set', "replicaCount=$replicas",
        '--set', "pdb.enabled=$($pdb.ToString().ToLowerInvariant())"
    )
    if (-not $serviceDiscovery) {
        $helmArguments += @('--set-string', "services.$serviceName=$serviceUrl")
    }

    Write-Host "[3/5] Instalando release '$release' no namespace '$namespace'..." -ForegroundColor Cyan
    Invoke-ClusterTool -Tool 'helm' -Arguments $helmArguments -FailureMessage 'A instalação Helm falhou.'

    Write-Host '[4/5] Validando o rollout...' -ForegroundColor Cyan
    $deployment = (Invoke-ClusterTool -Tool 'kubectl' -Arguments @(
        'get', 'deployment', '-n', $namespace,
        '-l', "app.kubernetes.io/instance=$release",
        '-o', 'jsonpath={.items[0].metadata.name}'
    ) -FailureMessage 'Não foi possível localizar o Deployment da release.').Trim()
    if (-not $deployment) { throw 'O Deployment da release não foi encontrado.' }
    Invoke-ClusterTool -Tool 'kubectl' -Arguments @(
        'rollout', 'status', "deployment/$deployment",
        '--namespace', $namespace,
        '--timeout=180s'
    ) -FailureMessage 'O rollout não ficou pronto no tempo esperado.'

    Write-Host '[5/5] Validando requisitos efetivos e readiness dentro do cluster...' -ForegroundColor Cyan
    Invoke-ClusterTool -Tool 'helm' -Arguments @(
        'test', $release,
        '--namespace', $namespace,
        '--timeout', '2m'
    ) -FailureMessage 'O teste de requisitos/readiness do chart falhou.'

    $serviceResource = (Invoke-ClusterTool -Tool 'kubectl' -Arguments @(
        'get', 'service', '-n', $namespace,
        '-l', "app.kubernetes.io/instance=$release",
        '-o', 'jsonpath={.items[0].metadata.name}'
    ) -FailureMessage 'Não foi possível localizar o Service da release.').Trim()

    Write-Host ''
    Write-Host 'doctor-api-mcp instalado.' -ForegroundColor Green
    Write-Host "Modo: $mode"
    Write-Host ''
    Write-Host 'Abra o acesso local:'
    if ($useWsl) {
        Write-Host "  wsl kubectl port-forward service/$serviceResource 4000:4000 -n $namespace"
    }
    else {
        Write-Host "  kubectl port-forward service/$serviceResource 4000:4000 -n $namespace"
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
    }
    else {
        Write-Warning "Diretório temporário inesperado; limpeza ignorada: $resolvedTempDir"
    }
}
