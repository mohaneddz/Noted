# Builds Noted-Setup.msi from scratch: publish the app, then package it with WiX.
#
# Requires the WiX CLI as a global dotnet tool (`dotnet tool install --global wix`)
# with its EULA accepted (`wix eula accept wix7`) and the UI extension installed
# (`wix extension add WixToolset.UI.wixext --global`).
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "build\publish"
$installerDir = Join-Path $root "build\installer"
$outMsi = Join-Path $root "build\Noted-Setup.msi"

Write-Host "Publishing Noted ($Configuration, framework-dependent, win-x64)..."
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish "$root\src\Noted" -c $Configuration -r win-x64 --self-contained false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Building installer..."
Push-Location $installerDir
try {
    wix build Product.wxs -ext WixToolset.UI.wixext -arch x64 -o $outMsi
    if ($LASTEXITCODE -ne 0) { throw "wix build failed" }
}
finally {
    Pop-Location
}

Write-Host "Done: $outMsi"
