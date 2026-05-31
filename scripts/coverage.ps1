param(
    [switch]$Open
)

$projectRoot = Split-Path $PSScriptRoot -Parent
$testProject = Join-Path $projectRoot "Boutique.Tests" "Boutique.Tests.csproj"
$runSettings = Join-Path $projectRoot "coverlet.runsettings"
$coverageDir = Join-Path $projectRoot "artifacts" "coverage"
$reportDir = Join-Path $coverageDir "report"

if (Test-Path $coverageDir) {
    Remove-Item $coverageDir -Recurse -Force
}

Write-Host "Running tests with coverage..." -ForegroundColor Cyan
dotnet test $testProject `
    --collect:"XPlat Code Coverage" `
    --settings $runSettings `
    --results-directory $coverageDir

$coverageFile = Get-ChildItem -Path $coverageDir -Filter "coverage.cobertura.xml" -Recurse | Select-Object -First 1

if (-not $coverageFile) {
    Write-Host "No coverage file generated." -ForegroundColor Red
    exit 1
}

Write-Host "Generating HTML report..." -ForegroundColor Cyan
reportgenerator `
    "-reports:$($coverageFile.FullName)" `
    "-targetdir:$reportDir" `
    "-reporttypes:Html;TextSummary" `
    "-assemblyfilters:+Boutique;-Boutique.Tests"

$summaryFile = Join-Path $reportDir "Summary.txt"
if (Test-Path $summaryFile) {
    Get-Content $summaryFile
}

$indexFile = Join-Path $reportDir "index.html"
Write-Host "Report generated at: $indexFile" -ForegroundColor Green

if ($Open -or -not $PSBoundParameters.ContainsKey('Open')) {
    Start-Process $indexFile
}
