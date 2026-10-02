param(
    [switch]$CheckOnly
)

$ErrorActionPreference = "Stop"
$projectPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$configurationPath = Join-Path $projectPath "configuracion-local.env"

Set-Location $projectPath

if (-not (Test-Path -LiteralPath $configurationPath)) {
    throw "No se encontró configuracion-local.env."
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker no está disponible en esta terminal."
}

$containerId = docker compose ps -q mysql
if ([string]::IsNullOrWhiteSpace($containerId)) {
    throw "El contenedor MySQL no está iniciado. Ejecuta primero docker compose up -d."
}

if ($CheckOnly) {
    Write-Host "Verificación correcta: Docker, MySQL y configuracion-local.env están disponibles." -ForegroundColor Green
    exit 0
}

Write-Host "Se generará una nueva contraseña segura para el usuario MySQL del CRM." -ForegroundColor Cyan
$confirmation = Read-Host "Escribe CAMBIAR para continuar"
if ($confirmation -cne "CAMBIAR") {
    Write-Host "Operación cancelada. No se realizó ningún cambio." -ForegroundColor Yellow
    exit 0
}

$alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!@%_-"
$randomBytes = New-Object byte[] 32
$randomGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $randomGenerator.GetBytes($randomBytes)
}
finally {
    $randomGenerator.Dispose()
}
$newPassword = "Crm!" + (-join ($randomBytes | ForEach-Object {
    $alphabet[[int]$_ % $alphabet.Length]
}))

$configuration = Get-Content -LiteralPath $configurationPath -Raw
$databaseUserMatch = [regex]::Match($configuration, '(?m)^MYSQL_USER=(.+)$')
if (-not $databaseUserMatch.Success) {
    throw "Falta MYSQL_USER en configuracion-local.env."
}

$databasePasswordMatch = [regex]::Match($configuration, '(?m)^MYSQL_PASSWORD=.*$')
if (-not $databasePasswordMatch.Success) {
    throw "Falta MYSQL_PASSWORD en configuracion-local.env. No se realizo ningun cambio."
}

$databaseUser = $databaseUserMatch.Groups[1].Value.Trim().Trim('"', "'")
$sqlUser = $databaseUser.Replace("'", "''")
$sqlPassword = $newPassword.Replace("'", "''")
$sql = "ALTER USER '$sqlUser'@'%' IDENTIFIED BY '$sqlPassword';"

# Usa la contraseña que ya está cargada dentro del contenedor. La contraseña
# nueva viaja por la entrada estándar y no queda escrita en el comando.
$sql | docker compose exec -T mysql sh -lc 'MYSQL_PWD=$MYSQL_PASSWORD mysql -h127.0.0.1 -u$MYSQL_USER $MYSQL_DATABASE'
if ($LASTEXITCODE -ne 0) {
    throw "MySQL no pudo cambiar la contraseña. El archivo local no fue modificado."
}

$updatedConfiguration = [regex]::Replace(
    $configuration,
    '(?m)^MYSQL_PASSWORD=.*$',
    "MYSQL_PASSWORD=$newPassword")

if ($updatedConfiguration -eq $configuration) {
    throw "MySQL cambió la contraseña, pero no se encontró MYSQL_PASSWORD en configuracion-local.env."
}

[System.IO.File]::WriteAllText(
    $configurationPath,
    $updatedConfiguration,
    [System.Text.UTF8Encoding]::new($false))

docker compose up -d --force-recreate
if ($LASTEXITCODE -ne 0) {
    throw "La contraseña fue actualizada, pero Docker no pudo recrear el contenedor."
}

Write-Host "Contraseña MySQL actualizada correctamente." -ForegroundColor Green
Write-Host "La nueva clave quedó guardada en configuracion-local.env." -ForegroundColor Green
Write-Host "Ahora detén y vuelve a ejecutar el CRM en Visual Studio." -ForegroundColor Cyan

