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

# If version is provided, update the csproj
if ($Version -ne "")
{
    Write-Host "Updating version to $Version..." -ForegroundColor Cyan
    
    # Read the csproj content
    $csprojContent = Get-Content $projectPath -Raw
    
    # Update version properties using regex
    $csprojContent = $csprojContent -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
    $csprojContent = $csprojContent -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>"
    $csprojContent = $csprojContent -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$Version.0</FileVersion>"
    
    # Write back
    Set-Content -Path $projectPath -Value $csprojContent -NoNewline
    Write-Host "Version updated to $Version" -ForegroundColor Green
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

# Create the zip
Compress-Archive -Path "$tempZipDir\*" -DestinationPath $zipPath -Force

# Clean up temp directory
Remove-Item $tempZipDir -Recurse -Force

Write-Host "Distribution zip created: $zipPath" -ForegroundColor Green

# Show next steps
Write-Host ""
Write-Host "=== Release Checklist ===" -ForegroundColor Magenta
Write-Host "1. Create a GitHub release with tag 'v$Version' (or '$Version-alpha', etc.)" -ForegroundColor White
Write-Host "2. Upload 'artifacts/publish/Boutique.zip' to the release" -ForegroundColor White
Write-Host ""
Write-Host "The app will automatically detect the new release from GitHub!" -ForegroundColor Green
Write-Host ""
