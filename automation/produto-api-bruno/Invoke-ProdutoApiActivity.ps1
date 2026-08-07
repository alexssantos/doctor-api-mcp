[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$BaseUrl = "http://localhost:5002",

    [ValidateRange(1, 3600)]
    [int]$DurationSeconds = 300,

    [ValidateRange(1, 60)]
    [int]$IntervalSeconds = 8,

    [ValidateNotNullOrEmpty()]
    [string]$Environment = "local"
)

$ErrorActionPreference = "Stop"
$collectionPath = $PSScriptRoot

if (-not (Get-Command bru -ErrorAction SilentlyContinue)) {
    throw "Bruno CLI nao encontrado. Instale-o com: npm install --global @usebruno/cli"
}

$startedAt = Get-Date
$deadline = $startedAt.AddSeconds($DurationSeconds)
$round = 0

Write-Host "Iniciando simulacao no $BaseUrl por $DurationSeconds segundos."

while ((Get-Date) -lt $deadline) {
    $round++
    Write-Host "Rodada ${round}: executando CRUD de produto."

    & bru run $collectionPath --env $Environment --env-var "baseUrl=$BaseUrl" --bail
    if ($LASTEXITCODE -ne 0) {
        throw "A rodada $round falhou. A simulacao foi interrompida."
    }

    $remainingSeconds = [math]::Max(0, [int]($deadline - (Get-Date)).TotalSeconds)
    if ($remainingSeconds -gt 0) {
        Start-Sleep -Seconds ([math]::Min($IntervalSeconds, $remainingSeconds))
    }
}

Write-Host "Simulacao concluida. Rodadas executadas: $round."