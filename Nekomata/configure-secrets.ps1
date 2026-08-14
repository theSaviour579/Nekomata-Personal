[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Nekomata\Nekomata.UI.csproj'

if (-not (Test-Path -LiteralPath $project)) {
    throw "Nekomata.UI.csproj was not found at $project"
}

function Read-RequiredText([string]$Prompt) {
    do { $value = Read-Host $Prompt } while ([string]::IsNullOrWhiteSpace($value))
    return $value.Trim()
}

function Read-SecretText([string]$Prompt) {
    $secure = Read-Host $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Set-Secret([string]$Key, [string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return }
    & dotnet user-secrets set $Key $Value --project $project | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to save $Key" }
    Write-Host "Saved $Key" -ForegroundColor Green
}

Write-Host 'Nekomata local secrets setup' -ForegroundColor Cyan
Write-Host 'Values are stored in the Windows .NET user-secrets store, not appsettings.json.'
Write-Host ''

Set-Secret 'OpenAI:ApiKey' (Read-SecretText 'OpenAI API key')
Set-Secret 'Database:Password' (Read-SecretText 'PostgreSQL password')
Set-Secret 'Halo:BaseUrl' (Read-RequiredText 'Halo base URL (for example https://tenant.haloitsm.com)')
Set-Secret 'Halo:Tenant' (Read-RequiredText 'Halo tenant name')
Set-Secret 'Halo:ClientId' (Read-RequiredText 'Halo client ID')
Set-Secret 'Halo:ClientSecret' (Read-SecretText 'Halo client secret')
Set-Secret 'Halo:Username' (Read-Host 'Halo username (optional)')
Set-Secret 'Halo:Password' (Read-SecretText 'Halo password (optional; press Enter to skip)')

Write-Host ''
Write-Host 'Configuration complete. Restart Nekomata to load the secrets.' -ForegroundColor Cyan
