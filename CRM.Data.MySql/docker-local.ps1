[CmdletBinding()]
param(
    [ValidateSet("up", "stop", "start", "restart", "status", "logs")]
    [string]$Action = "up"
)

$ErrorActionPreference = "Stop"
$composePath = Join-Path $PSScriptRoot "docker-compose.yml"
$environmentPath = Join-Path $PSScriptRoot "configuracion-local.env"

if (-not (Test-Path -LiteralPath $environmentPath)) {
    throw "Falta el archivo local '$environmentPath'."
}

$compose = @("compose", "-f", $composePath)

switch ($Action) {
        "up"      {
            & docker volume inspect crm_data_mysql *> $null
            if ($LASTEXITCODE -ne 0) {
                & docker volume create crm_data_mysql | Out-Null
            }
            & docker @compose up -d
        }
        "stop"    { & docker @compose stop }
        "start"   { & docker @compose start }
        "restart" { & docker @compose restart }
        "status"  { & docker @compose ps }
        "logs"    { & docker @compose logs --tail 100 }
}

if ($LASTEXITCODE -ne 0) {
    throw "Docker Compose terminó con código $LASTEXITCODE."
}
