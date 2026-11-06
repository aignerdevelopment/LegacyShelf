param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "Building LegacyShelf ($Configuration)…"
dotnet build (Join-Path $root "LegacyShelf.sln") -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$output = Join-Path $root "src\LegacyShelf\bin\$Configuration\net48"
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null

$stage = Join-Path $env:TEMP ("LegacyShelf-stage-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $stage | Out-Null

Copy-Item (Join-Path $output "LegacyShelf.exe") $stage
$config = Join-Path $output "LegacyShelf.exe.config"
if (Test-Path $config) { Copy-Item $config $stage }
Copy-Item (Join-Path $root "README.md") $stage
Copy-Item (Join-Path $root "LICENSE") $stage

$zip = Join-Path $dist "LegacyShelf-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Remove-Item $stage -Recurse -Force

Write-Host ""
Write-Host "Release: $zip"
