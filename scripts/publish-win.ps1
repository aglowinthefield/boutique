param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "..\Boutique.csproj"
$outputRoot = Join-Path $PSScriptRoot "..\artifacts\publish"
$publishPath = Join-Path $outputRoot $Runtime

if (-not (Test-Path $projectPath))
{
    throw "Could not locate project file at '$projectPath'. Run the script from within the repository."
}

if (-not (Test-Path $outputRoot))
{
    New-Item -ItemType Directory -Path $outputRoot | Out-Null
}

if (-not (Test-Path $publishPath))
{
    New-Item -ItemType Directory -Path $publishPath | Out-Null
}

# Kill any existing Boutique processes
$boutiqueProcesses = Get-Process -Name "Boutique" -ErrorAction SilentlyContinue
if ($boutiqueProcesses)
{
    Write-Host "Stopping existing Boutique processes..." -ForegroundColor Yellow
    $boutiqueProcesses | Stop-Process -Force
    Start-Sleep -Seconds 1
    Write-Host "Boutique processes stopped." -ForegroundColor Green
}

$selfContainedValue = $SelfContained.ToString().ToLower()

$arguments = @(
    "publish", $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContainedValue,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "--output", $publishPath
)

# Version is auto-detected from git tags via MinVer
# Use -Version parameter to override if needed (e.g., for testing)
if ($Version -ne "")
{
    Write-Host "Overriding MinVer version to $Version..." -ForegroundColor Cyan
    $arguments += "-p:MinVerVersionOverride=$Version"
}

Write-Host "Publishing Boutique ($Configuration | $Runtime | SelfContained=$selfContainedValue)..." -ForegroundColor Cyan
Write-Host "Output: $publishPath" -ForegroundColor Cyan

dotnet @arguments

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Publish complete." -ForegroundColor Green

# Create zip file for distribution
$zipPath = Join-Path $outputRoot "Boutique.zip"

# Remove old zip if exists
if (Test-Path $zipPath)
{
    Remove-Item $zipPath -Force
}

Write-Host "Creating distribution zip..." -ForegroundColor Cyan

# Get just the exe file (and pdb for debugging if present)
$exePath = Join-Path $publishPath "Boutique.exe"
if (-not (Test-Path $exePath))
{
    throw "Boutique.exe not found at $exePath"
}

# Create a temp directory for the zip contents
$tempZipDir = Join-Path $outputRoot "temp_zip"
if (Test-Path $tempZipDir)
{
    Remove-Item $tempZipDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempZipDir | Out-Null

# Copy the exe to temp directory
Copy-Item $exePath $tempZipDir

# Copy satellite assemblies (translations) from build output
# Single-file publish doesn't include these, so we get them from the intermediate build folder
$buildOutputPath = Join-Path $PSScriptRoot "..\bin\$Configuration\net8.0-windows10.0.19041\$Runtime"
if (-not (Test-Path $buildOutputPath))
{
    $buildOutputPath = Join-Path $PSScriptRoot "..\bin\$Configuration\net8.0-windows10.0.19041"
}

$cultureFolders = @(Get-ChildItem -Path $buildOutputPath -Directory -ErrorAction SilentlyContinue | Where-Object {
    $_.Name -match "^[a-z]{2}(-[A-Za-z]{2,})?$" -and (Test-Path (Join-Path $_.FullName "Boutique.resources.dll"))
})

foreach ($folder in $cultureFolders)
{
    $destFolder = Join-Path $tempZipDir $folder.Name
    New-Item -ItemType Directory -Path $destFolder -Force | Out-Null
    Copy-Item (Join-Path $folder.FullName "Boutique.resources.dll") $destFolder
    Write-Host "  Including translation: $( $folder.Name )" -ForegroundColor DarkGray
}

if ($cultureFolders.Count -gt 0)
{
    Write-Host "Included $( $cultureFolders.Count ) translation(s)" -ForegroundColor Green
}

# Create the zip
Compress-Archive -Path "$tempZipDir\*" -DestinationPath $zipPath -Force

# Clean up temp directory
Remove-Item $tempZipDir -Recurse -Force

Write-Host "Distribution zip created: $zipPath" -ForegroundColor Green

# Show next steps
Write-Host ""
Write-Host "=== Release Workflow ===" -ForegroundColor Magenta
Write-Host "Version is auto-detected from git tags via MinVer." -ForegroundColor White
Write-Host "To release:" -ForegroundColor White
Write-Host "  1. Tag the commit: git tag v1.2.3" -ForegroundColor White
Write-Host "  2. Build: pwsh scripts/publish-win.ps1" -ForegroundColor White
Write-Host "  3. Push tag: git push origin v1.2.3" -ForegroundColor White
Write-Host "  4. Create GitHub release and upload artifacts/publish/Boutique.zip" -ForegroundColor White
Write-Host ""
