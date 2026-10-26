param(
    [Parameter(Mandatory = $true)]
    [string] $Destination
)

$ErrorActionPreference = 'Stop'
$expectedSha256 = '0CFD5E79AAB70A3D8C1A57DC639835110DDB32C9F5FF4FDD1F4DB202EA43BB05'
$url = 'https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro.onnx'
$parent = Split-Path -Parent $Destination
if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }

if (Test-Path -LiteralPath $Destination) {
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $Destination).Hash
    if ($actual -eq $expectedSha256) {
        Write-Host 'Using cached, verified Kokoro model.'
        exit 0
    }
    Remove-Item -LiteralPath $Destination -Force
}

Invoke-WebRequest -Uri $url -OutFile $Destination
$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $Destination).Hash
if ($actual -ne $expectedSha256) {
    Remove-Item -LiteralPath $Destination -Force
    throw "Kokoro model checksum mismatch. Expected $expectedSha256 but received $actual."
}

Write-Host 'Downloaded and verified the Kokoro model.'
