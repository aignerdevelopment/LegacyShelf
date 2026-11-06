param(
    [string]$Configuration = "Release",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$buildArgs = @(
    (Join-Path $root "LegacyShelf.sln"),
    "-c", $Configuration,
    "--nologo"
)

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version must be numeric semver, e.g. 1.0.0 (got '$Version')."
    }
    $buildArgs += "-p:Version=$Version"
    $buildArgs += "-p:FileVersion=$Version.0"
    $buildArgs += "-p:AssemblyVersion=$Version.0"
    Write-Host "Building LegacyShelf $Version ($Configuration)…"
}
else {
    Write-Host "Building LegacyShelf ($Configuration)…"
}

dotnet build @buildArgs
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
$changelog = Join-Path $root "CHANGELOG.md"
if (Test-Path $changelog) { Copy-Item $changelog $stage }

$screenshots = Join-Path $root "docs\screenshots"
if (Test-Path $screenshots) {
    $screenshotStage = Join-Path $stage "docs\screenshots"
    New-Item -ItemType Directory -Force $screenshotStage | Out-Null
    Copy-Item (Join-Path $screenshots "*") $screenshotStage
}

$assetName = if ([string]::IsNullOrWhiteSpace($Version)) {
    "LegacyShelf-win-x64.zip"
} else {
    "LegacyShelf-$Version-win-x64.zip"
}

$zip = Join-Path $dist $assetName
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Remove-Item $stage -Recurse -Force

Write-Host ""
Write-Host "Release: $zip"
