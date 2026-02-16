#!/usr/bin/env pwsh
# GrblHAL Sender build script
# Usage: ./build.ps1 -Runtime win-x64 -Version 1.0.0
#        ./build.ps1 -Runtime linux-x64 -Version 1.0.0
#        ./build.ps1 -Runtime osx-arm64 -Version 1.0.0

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
    [string]$Runtime,

    [string]$Version = "0.0.1-local",

    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "GrbLHALSender.Desktop" "GrbLHALSender.Desktop.csproj"
$PublishDir = Join-Path $RepoRoot "publish" $Runtime
$ArtifactsDir = Join-Path $RepoRoot "artifacts"

# Ensure artifacts directory exists
New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null

Write-Host "Building GrblHAL Sender $Version for $Runtime..." -ForegroundColor Cyan

# Publish
dotnet publish $ProjectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:Version=$Version `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Published to: $PublishDir" -ForegroundColor Green

# Platform-specific packaging
$platform = $Runtime.Split("-")[0]
switch ($platform) {
    "win" {
        $arch = $Runtime.Replace("win-", "")
        $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
        if (Test-Path $iscc) {
            Write-Host "Building Windows installer..." -ForegroundColor Cyan
            & $iscc /DAppVersion=$Version /DArch=$arch /DPublishDir=$PublishDir `
                (Join-Path $RepoRoot "installer" "windows" "GrblHALSender.iss")
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        } else {
            Write-Host "Inno Setup not found at $iscc - skipping installer creation" -ForegroundColor Yellow
            Write-Host "Install from: https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
        }
    }
    "linux" {
        $debArch = if ($Runtime -eq "linux-x64") { "amd64" } else { "arm64" }
        $script = Join-Path $RepoRoot "installer" "linux" "build-deb.sh"
        if (Test-Path $script) {
            Write-Host "Building .deb package..." -ForegroundColor Cyan
            chmod +x $script
            & bash $script $Version $debArch $PublishDir
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
    }
    "osx" {
        $arch = $Runtime.Replace("osx-", "")
        $script = Join-Path $RepoRoot "installer" "macos" "build-dmg.sh"
        if (Test-Path $script) {
            Write-Host "Building .dmg package..." -ForegroundColor Cyan
            chmod +x $script
            & bash $script $Version $arch $PublishDir
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
    }
}

Write-Host "Build complete!" -ForegroundColor Green
