param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent
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

$selfContained = if ($FrameworkDependent)
{
    "false"
}
else
{
    "true"
}

$arguments = @(
    "publish", $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContained,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "--output", $publishPath
)

Write-Host "Publishing Boutique ($Configuration | $Runtime | SelfContained=$selfContained)..." -ForegroundColor Cyan
Write-Host "Output: $publishPath" -ForegroundColor Cyan

dotnet @arguments

Write-Host "Publish complete." -ForegroundColor Green
