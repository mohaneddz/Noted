# Builds Noted-Setup.msi from scratch: restore, publish the app, then package it with WiX.
# Installs the WiX CLI as a global dotnet tool automatically if it isn't already present.
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "build\publish"
$installerDir = Join-Path $root "build\installer"
$outMsi = Join-Path $root "build\Noted-Setup.msi"

Write-Host "Restoring solution..."
dotnet restore (Join-Path $root "Noted.slnx")
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

Write-Host "Checking for WiX CLI..."
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    Write-Host "WiX CLI not found - installing as a global dotnet tool..."
    dotnet tool install --global wix
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool install wix failed" }
    Write-Host "Accepting WiX EULA and adding UI extension..."
    wix eula accept wix7
    wix extension add WixToolset.UI.wixext --global
}

Write-Host "Publishing Noted ($Configuration, framework-dependent, win-x64)..."
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish "$root\src\Noted" -c $Configuration -r win-x64 --self-contained false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Building installer..."
Push-Location $installerDir
try {
    # Read product version from project file so the MSI version matches the app version
    $csprojPath = Join-Path $root "src\Noted\Noted.csproj"
    if (Test-Path $csprojPath) {
        [xml]$csproj = Get-Content $csprojPath
        $verNode = $csproj.Project.PropertyGroup.Version
        if ($verNode) { $productVersion = $verNode.Trim() } else { $productVersion = "1.0.0" }
    } else {
        $productVersion = "1.0.0"
    }

    Write-Host "Building MSI with ProductVersion=$productVersion"

    # Create a temporary Product.wxs with the concrete Version attribute because the wix CLI used here
    # does not accept preprocessor defines via this wrapper reliably.
    $orig = Get-Content -Raw -Path 'Product.wxs'
    $pattern = 'Version="\$\(var\.ProductVersion\)"'
    $replacement = "Version=`"$productVersion`""
    $tempContent = [System.Text.RegularExpressions.Regex]::Replace($orig, $pattern, $replacement)
    $tempWxs = Join-Path $installerDir "Product.temp.wxs"
    Set-Content -Path $tempWxs -Value $tempContent -Encoding UTF8

    $wixArgs = @(
        'build',
        'Product.temp.wxs',
        '-ext', 'WixToolset.UI.wixext',
        '-arch', 'x64',
        '-o', $outMsi
    )
    & wix @wixArgs
    $rc = $LASTEXITCODE
    Remove-Item -Force $tempWxs
    if ($rc -ne 0) { throw "wix build failed" }
}
finally {
    Pop-Location
}

Write-Host "Done: $outMsi"
