#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('installer', 'runtime', 'all')]
    [string]$Phase = 'installer',

    [string]$Namespace = 'mcp-apis',
    [string]$Release = 'doctor-api-mcp',

    [ValidateSet('Cluster', 'Namespace', 'None')]
    [string]$Scope = 'Cluster',

    [bool]$ServiceDiscovery = $true,

    [ValidateSet('ConfigMap', 'Memory')]
    [string]$StateStorage = 'ConfigMap',

    [bool]$DeploymentEvents = $true,
    [bool]$NetworkPolicy = $false,
    [bool]$Ingress = $false,
    [bool]$Pdb = $true,
    [string]$Context = ''
)

$ErrorActionPreference = 'Stop'
$script:PassCount = 0
$script:FailCount = 0
$script:KubectlPrefix = @()
if ($Context) { $script:KubectlPrefix = @('--context', $Context) }

if (-not (Get-Command kubectl -ErrorAction SilentlyContinue)) {
    throw 'kubectl não foi encontrado no PATH.'
}

function Write-Pass([string]$Message) {
    Write-Host "  [PASS] $Message" -ForegroundColor Green
    $script:PassCount++
}

function Write-Fail([string]$Message) {
    Write-Host "  [FAIL] $Message" -ForegroundColor Red
    $script:FailCount++
}

function Write-Section([string]$Title) {
    Write-Host "`n=== $Title ===" -ForegroundColor Cyan
}

function Get-KubectlText([string[]]$Arguments) {
    $output = & kubectl @script:KubectlPrefix @Arguments 2>$null
    return (@($output) -join "`n").Trim()
}

function Test-CanI {
    param(
        [Parameter(Mandatory)][ValidateSet('yes', 'no')][string]$Expected,
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    $actual = (Get-KubectlText (@('auth', 'can-i') + $Arguments)).ToLowerInvariant()
    if ($actual -eq $Expected) {
        Write-Pass "$Label -> $Expected"
    }
    else {
        if (-not $actual) { $actual = 'no-result' }
        Write-Fail "$Label -> expected $Expected, got $actual"
    }
}

function Test-ResourceVerbs {
    param(
        [Parameter(Mandatory)][string]$Resource,
        [Parameter(Mandatory)][string[]]$Verbs,
        [Parameter(Mandatory)][string]$Label,
        [switch]$ClusterScoped
    )

    foreach ($verb in $Verbs) {
        $arguments = @($verb, $Resource)
        if (-not $ClusterScoped) { $arguments += @('-n', $Namespace) }
        Test-CanI -Expected yes -Label "$Label can $verb $Resource" -Arguments $arguments
    }
}

function Test-InstallerRequirements {
    Write-Section 'Installer permissions'
    & kubectl @script:KubectlPrefix version --request-timeout=5s *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Fail 'Kubernetes API is reachable'
        return
    }
    Write-Pass 'Kubernetes API is reachable'

    & kubectl @script:KubectlPrefix get namespace $Namespace *> $null
    if ($LASTEXITCODE -eq 0) {
        Write-Pass "namespace/$Namespace already exists"
    }
    else {
        Test-CanI -Expected yes -Label "installer can create namespace/$Namespace" `
            -Arguments @('create', 'namespaces')
    }

    $standardVerbs = @('get', 'create', 'update', 'patch', 'delete')
    $resources = @(
        'serviceaccounts',
        'configmaps',
        'secrets',
        'services',
        'deployments.apps'
    )
    foreach ($resource in $resources) {
        Test-ResourceVerbs -Resource $resource -Verbs $standardVerbs -Label 'installer'
    }

    # Helm stores release revisions as Secrets and the readiness hook is a Pod.
    Test-CanI -Expected yes -Label 'installer can list Helm release secrets' `
        -Arguments @('list', 'secrets', '-n', $Namespace)
    foreach ($verb in @('list', 'watch')) {
        Test-CanI -Expected yes -Label "installer can $verb deployments.apps" `
            -Arguments @($verb, 'deployments.apps', '-n', $Namespace)
    }
    Test-ResourceVerbs -Resource 'pods' -Verbs @('get', 'list', 'watch', 'create', 'delete') `
        -Label 'installer'

    if ($Scope -eq 'Namespace' -or $StateStorage -eq 'ConfigMap') {
        foreach ($resource in @('roles.rbac.authorization.k8s.io', 'rolebindings.rbac.authorization.k8s.io')) {
            Test-ResourceVerbs -Resource $resource -Verbs $standardVerbs -Label 'installer'
        }
    }

    if ($Pdb) {
        Test-ResourceVerbs -Resource 'poddisruptionbudgets.policy' -Verbs $standardVerbs -Label 'installer'
    }
    if ($NetworkPolicy) {
        Test-ResourceVerbs -Resource 'networkpolicies.networking.k8s.io' -Verbs $standardVerbs -Label 'installer'
    }
    if ($Ingress) {
        Test-ResourceVerbs -Resource 'ingresses.networking.k8s.io' -Verbs $standardVerbs -Label 'installer'
    }

    if ($Scope -eq 'Cluster') {
        foreach ($resource in @('clusterroles.rbac.authorization.k8s.io', 'clusterrolebindings.rbac.authorization.k8s.io')) {
            Test-ResourceVerbs -Resource $resource -Verbs $standardVerbs -Label 'cluster mode installer' -ClusterScoped
        }
    }
    else {
        Write-Pass "$($Scope.ToLowerInvariant()) mode requires no cluster-scoped object creation"
    }
}

function Test-RuntimeRequirements {
    Write-Section 'Runtime service-account permissions'
    $deployment = Get-KubectlText @(
        'get', 'deployment', '-n', $Namespace,
        '-l', "app.kubernetes.io/instance=$Release",
        '-o', 'jsonpath={.items[0].metadata.name}'
    )
    if (-not $deployment) {
        Write-Fail "deployment for Helm release $Release exists"
        return
    }
    Write-Pass "deployment/$deployment found"

    $serviceAccount = Get-KubectlText @(
        'get', 'deployment', $deployment, '-n', $Namespace,
        '-o', 'jsonpath={.spec.template.spec.serviceAccountName}'
    )
    if (-not $serviceAccount) {
        Write-Fail 'runtime ServiceAccount is configured'
        return
    }
    Write-Pass "runtime ServiceAccount is $serviceAccount"
    $identity = "system:serviceaccount:${Namespace}:${serviceAccount}"

    function Test-RuntimeCanI {
        param(
            [Parameter(Mandatory)][ValidateSet('yes', 'no')][string]$Expected,
            [Parameter(Mandatory)][string]$Label,
            [Parameter(Mandatory)][string[]]$Arguments
        )
        Test-CanI -Expected $Expected -Label $Label -Arguments (@("--as=$identity") + $Arguments)
    }

    if ($Scope -eq 'None') {
        Test-RuntimeCanI -Expected no -Label 'Scope None cannot list pods' `
            -Arguments @('list', 'pods', '-n', $Namespace)
        Test-RuntimeCanI -Expected no -Label 'Scope None cannot list services' `
            -Arguments @('list', 'services', '-n', $Namespace)
        $automount = Get-KubectlText @(
            'get', 'deployment', $deployment, '-n', $Namespace,
            '-o', 'jsonpath={.spec.template.spec.automountServiceAccountToken}'
        )
        if ($automount -eq 'false') {
            Write-Pass 'Scope None disables ServiceAccount token automount'
        }
        else {
            if (-not $automount) { $automount = 'unset' }
            Write-Fail "Scope None disables ServiceAccount token automount (got $automount)"
        }
        return
    }

    $scopeArguments = if ($Scope -eq 'Cluster') { @('--all-namespaces') } else { @('-n', $Namespace) }
    foreach ($resource in @('pods', 'deployments.apps')) {
        foreach ($verb in @('list', 'get')) {
            Test-RuntimeCanI -Expected yes -Label "runtime can $verb $resource in declared scope" `
                -Arguments (@($verb, $resource) + $scopeArguments)
        }
    }

    if ($DeploymentEvents) {
        foreach ($verb in @('list', 'get')) {
            Test-RuntimeCanI -Expected yes -Label "runtime can $verb events in declared scope" `
                -Arguments (@($verb, 'events') + $scopeArguments)
        }
    }

    if ($ServiceDiscovery) {
        foreach ($resource in @('services', 'endpoints')) {
            foreach ($verb in @('list', 'get')) {
                Test-RuntimeCanI -Expected yes -Label "runtime can $verb $resource in declared scope" `
                    -Arguments (@($verb, $resource) + $scopeArguments)
            }
        }
    }
    else {
        Test-RuntimeCanI -Expected no -Label 'service discovery disabled: cannot list services' `
            -Arguments @('list', 'services', '-n', $Namespace)
        Test-RuntimeCanI -Expected no -Label 'service discovery disabled: cannot list endpoints' `
            -Arguments @('list', 'endpoints', '-n', $Namespace)
    }

    if ($Scope -eq 'Namespace') {
        Test-RuntimeCanI -Expected no -Label 'namespace mode cannot list pods cluster-wide' `
            -Arguments @('list', 'pods', '--all-namespaces')
        Test-RuntimeCanI -Expected no -Label 'namespace mode cannot list services cluster-wide' `
            -Arguments @('list', 'services', '--all-namespaces')
    }

    if ($StateStorage -eq 'ConfigMap') {
        $stateConfigMap = Get-KubectlText @(
            'get', 'configmap', "$deployment-config", '-n', $Namespace,
            '-o', 'jsonpath={.data.Discovery__StateConfigMap}'
        )
        if (-not $stateConfigMap) { $stateConfigMap = "$deployment-state" }
        foreach ($verb in @('get', 'update', 'patch')) {
            Test-RuntimeCanI -Expected yes -Label "runtime can $verb its state ConfigMap" `
                -Arguments @($verb, "configmap/$stateConfigMap", '-n', $Namespace)
        }
    }
    else {
        Write-Pass 'memory state requires no ConfigMap permission'
    }
}

switch ($Phase) {
    'installer' { Test-InstallerRequirements }
    'runtime' { Test-RuntimeRequirements }
    'all' {
        Test-InstallerRequirements
        Test-RuntimeRequirements
    }
}

Write-Host "`nINSTALL_REQUIREMENTS_SUMMARY:phase=$Phase`:pass=$script:PassCount`:fail=$script:FailCount"
if ($script:FailCount -gt 0) {
    throw "$($script:FailCount) requisito(s) de instalação não atendido(s)."
}
