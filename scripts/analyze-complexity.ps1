param(
  [string]$ProjectPath = (Join-Path $PSScriptRoot ".." "Boutique.csproj"),
  [int]$TopN = 30,
  [switch]$ShowAll,
  [switch]$JsonOutput,
  [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$ProjectPath = Resolve-Path $ProjectPath
$ProjectDir = Split-Path $ProjectPath

Write-Host "`n  Boutique Complexity Analysis" -ForegroundColor Cyan
Write-Host "  ===========================" -ForegroundColor Cyan

$buildOutput = $null
if (-not $NoBuild) {
  Write-Host "`n  Building project..." -ForegroundColor DarkGray
  dotnet clean $ProjectPath --nologo -v:q 2>&1 | Out-Null
  $rawOutput = dotnet build $ProjectPath --nologo 2>&1
  $exitCode = $LASTEXITCODE
  $buildOutput = $rawOutput | ForEach-Object { $_.ToString() }
  if ($exitCode -ne 0) {
    Write-Host "  Build failed with exit code $exitCode" -ForegroundColor Red
    $buildOutput | Where-Object { $_ -match "error" } | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
  }
}
else {
  $cacheFile = Join-Path $ProjectDir "artifacts" "build-output.txt"
  if (Test-Path $cacheFile) {
    $buildOutput = Get-Content $cacheFile
    Write-Host "`n  Using cached build output from $cacheFile" -ForegroundColor DarkGray
  }
  else {
    Write-Host "  No cached build output found. Run without -NoBuild first." -ForegroundColor Red
    exit 1
  }
}

$warningRules = @(
  "S3776",   # Cognitive complexity
  "S1541",   # Cyclomatic complexity (Sonar)
  "S107",    # Too many parameters
  "S138",    # Method too long
  "S104",    # File too long
  "S1200",   # Too many dependencies
  "CA1501",  # Depth of inheritance
  "CA1502",  # Cyclomatic complexity (Microsoft)
  "CA1505",  # Maintainability index
  "CA1506"   # Class coupling
)
$rulePattern = ($warningRules | ForEach-Object { [regex]::Escape($_) }) -join "|"
$warnings = $buildOutput | Where-Object { $_ -match "warning ($rulePattern):" }

Write-Host "  Found $($warnings.Count) complexity warnings`n" -ForegroundColor DarkGray

$parsed = @()
foreach ($w in $warnings) {
  if ($w -match '(?<file>[^(]+)\((?<line>\d+),(?<col>\d+)\):\s+warning\s+(?<rule>\w+):\s+(?<msg>.+?)\s+\(https?://') {
    $file = $Matches.file.Trim()
    $relFile = $file
    if ($file.StartsWith($ProjectDir)) {
      $relFile = $file.Substring($ProjectDir.Length).TrimStart('\', '/')
    }
    $fileName = Split-Path $file -Leaf

    $rule = $Matches.rule
    $msg = $Matches.msg
    $line = [int]$Matches.line
    $value = 0
    $memberName = ""

    switch -Regex ($rule) {
      "S3776" {
        if ($msg -match 'Cognitive Complexity from (\d+)') { $value = [int]$Matches[1] }
        if ($msg -match "this method '([^']+)'") { $memberName = $Matches[1] }
        elseif ($msg -match "this constructor '([^']+)'") { $memberName = "$($Matches[1])..ctor" }
        elseif ($msg -match "this (method|property|constructor)") { $memberName = "(see source)" }
      }
      "S1541" {
        if ($msg -match 'Cyclomatic Complexity of this \w+ is (\d+)') { $value = [int]$Matches[1] }
        if ($msg -match "this constructor '([^']+)'") { $memberName = "$($Matches[1])..ctor" }
        elseif ($msg -match "this \w+ is (\d+)") {
          $memberName = "(see source)"
        }
      }
      "S107" {
        if ($msg -match '(\d+) parameters') { $value = [int]$Matches[1] }
      }
      "S138" {
        if ($msg -match "has (\d+) lines") { $value = [int]$Matches[1] }
        if ($msg -match "'([^']+)'") { $memberName = $Matches[1] }
      }
      "S104" {
        if ($msg -match "has (\d+) lines") { $value = [int]$Matches[1] }
      }
      "CA1502" {
        if ($msg -match "cyclomatic complexity of '(\d+)'") { $value = [int]$Matches[1] }
        if ($msg -match "'([^']+)' has") { $memberName = $Matches[1] }
      }
      "CA1506" {
        if ($msg -match "coupled with '(\d+)'") { $value = [int]$Matches[1] }
        if ($msg -match "'([^']+)' is") { $memberName = $Matches[1] }
      }
    }

    $parsed += [PSCustomObject]@{
      File     = $fileName
      RelPath  = $relFile
      Line     = $line
      Rule     = $rule
      Value    = $value
      Member   = $memberName
      Message  = $msg
      Category = switch ($rule) {
        "S3776"  { "CognitiveComplexity" }
        "S1541"  { "CyclomaticComplexity" }
        "CA1502" { "CyclomaticComplexity" }
        "S107"   { "Parameters" }
        "S138"   { "MethodLength" }
        "S104"   { "FileLength" }
        "CA1506" { "ClassCoupling" }
        "CA1501" { "InheritanceDepth" }
        "CA1505" { "Maintainability" }
        "S1200"  { "ClassCoupling" }
        default  { "Other" }
      }
    }
  }
}

$parsed = $parsed |
  Group-Object { "$($_.File)|$($_.Line)|$($_.Rule)" } |
  ForEach-Object { $_.Group | Select-Object -First 1 }

$csFiles = Get-ChildItem -Path $ProjectDir -Filter "*.cs" -Recurse |
  Where-Object {
    $_.FullName -notmatch "\\(obj|bin|Boutique\.Tests)\\" -and
    $_.Name -ne "Strings.Designer.cs"
  }

$fileLoc = @()
foreach ($f in $csFiles) {
  $lineCount = (Get-Content $f.FullName | Measure-Object).Count
  $relPath = $f.FullName
  if ($f.FullName.StartsWith($ProjectDir)) {
    $relPath = $f.FullName.Substring($ProjectDir.Length).TrimStart('\', '/')
  }
  $parent = Split-Path $relPath
  $folder = if ($parent) { Split-Path $parent -Leaf } else { "(root)" }
  $fileLoc += [PSCustomObject]@{
    File     = $f.Name
    RelPath  = $relPath
    Lines    = $lineCount
    Folder   = $folder
  }
}

if ($JsonOutput) {
  @{
    generated = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    warnings  = $parsed
    files     = $fileLoc
  } | ConvertTo-Json -Depth 5
  return
}

function Write-Section($title) {
  Write-Host "`n  $title" -ForegroundColor Yellow
  Write-Host "  $("-" * $title.Length)" -ForegroundColor DarkYellow
}

function Pad($text, $width) {
  if ($text.Length -gt $width) { return $text.Substring(0, $width - 3) + "..." }
  return $text
}

Write-Section "SUMMARY BY CATEGORY"

$categories = $parsed | Group-Object Category | Sort-Object Count -Descending
foreach ($cat in $categories) {
  $color = if ($cat.Count -ge 20) { "Red" } elseif ($cat.Count -ge 10) { "Yellow" } else { "White" }
  Write-Host ("  {0,4}" -f $cat.Count) -ForegroundColor $color -NoNewline
  Write-Host "  $($cat.Name)"
}

Write-Host ""
$totalWarnings = $parsed.Count
Write-Host "  Total: " -NoNewline
Write-Host "$totalWarnings warnings" -ForegroundColor $(if ($totalWarnings -gt 50) { "Red" } elseif ($totalWarnings -gt 20) { "Yellow" } else { "Green" })

Write-Section "COGNITIVE COMPLEXITY HOTSPOTS (S3776 - hardest to understand)"

$cognitive = $parsed | Where-Object { $_.Rule -eq "S3776" } | Sort-Object Value -Descending
$limit = if ($ShowAll) { $cognitive.Count } else { [Math]::Min($TopN, $cognitive.Count) }
$cognitive | Select-Object -First $limit | ForEach-Object {
  $color = if ($_.Value -ge 30) { "Red" } elseif ($_.Value -ge 20) { "Yellow" } else { "White" }
  $name = Pad $_.Member 55
  Write-Host ("  {0,4}" -f $_.Value) -ForegroundColor $color -NoNewline
  Write-Host "  $(Pad $name 55)" -NoNewline
  Write-Host "  $($_.RelPath):$($_.Line)" -ForegroundColor DarkGray
}

Write-Section "CYCLOMATIC COMPLEXITY HOTSPOTS (CA1502 - most branching paths)"

$cyclomatic = $parsed | Where-Object { $_.Rule -eq "CA1502" } | Sort-Object Value -Descending
$limit = if ($ShowAll) { $cyclomatic.Count } else { [Math]::Min($TopN, $cyclomatic.Count) }
$cyclomatic | Select-Object -First $limit | ForEach-Object {
  $color = if ($_.Value -ge 40) { "Red" } elseif ($_.Value -ge 26) { "Yellow" } else { "White" }
  $name = Pad $_.Member 55
  Write-Host ("  {0,4}" -f $_.Value) -ForegroundColor $color -NoNewline
  Write-Host "  $(Pad $name 55)" -NoNewline
  Write-Host "  $($_.RelPath):$($_.Line)" -ForegroundColor DarkGray
}

Write-Section "CLASS COUPLING HOTSPOTS (CA1506 - most dependencies)"

$coupling = $parsed | Where-Object { $_.Rule -eq "CA1506" } | Sort-Object Value -Descending
$limit = if ($ShowAll) { $coupling.Count } else { [Math]::Min($TopN, $coupling.Count) }
$coupling | Select-Object -First $limit | ForEach-Object {
  $color = if ($_.Value -ge 80) { "Red" } elseif ($_.Value -ge 50) { "Yellow" } else { "White" }
  $name = Pad $_.Member 55
  Write-Host ("  {0,4}" -f $_.Value) -ForegroundColor $color -NoNewline
  Write-Host "  $(Pad $name 55)" -NoNewline
  Write-Host "  $($_.RelPath):$($_.Line)" -ForegroundColor DarkGray
}

Write-Section "METHOD LENGTH HOTSPOTS (S138 - longest methods)"

$lengths = $parsed | Where-Object { $_.Rule -eq "S138" } | Sort-Object Value -Descending
$limit = if ($ShowAll) { $lengths.Count } else { [Math]::Min($TopN, $lengths.Count) }
$lengths | Select-Object -First $limit | ForEach-Object {
  $color = if ($_.Value -ge 150) { "Red" } elseif ($_.Value -ge 80) { "Yellow" } else { "White" }
  $name = Pad $_.Member 55
  Write-Host ("  {0,4}" -f $_.Value) -ForegroundColor $color -NoNewline
  Write-Host " lines  $(Pad $name 55)" -NoNewline
  Write-Host "  $($_.RelPath):$($_.Line)" -ForegroundColor DarkGray
}

Write-Section "LARGEST FILES BY LINES OF CODE (top 20)"

$fileLoc | Sort-Object Lines -Descending | Select-Object -First 20 | ForEach-Object {
  $color = if ($_.Lines -ge 800) { "Red" } elseif ($_.Lines -ge 400) { "Yellow" } else { "White" }
  Write-Host ("  {0,5}" -f $_.Lines) -ForegroundColor $color -NoNewline
  Write-Host " lines  " -ForegroundColor DarkGray -NoNewline
  Write-Host $_.RelPath
}

Write-Section "WARNINGS PER FILE (files with most issues)"

$fileGroups = $parsed | Group-Object File | Sort-Object Count -Descending | Select-Object -First 20
foreach ($fg in $fileGroups) {
  $color = if ($fg.Count -ge 10) { "Red" } elseif ($fg.Count -ge 5) { "Yellow" } else { "White" }
  $ruleBreakdown = ($fg.Group | Group-Object Rule | ForEach-Object { "$($_.Name):$($_.Count)" }) -join " "
  Write-Host ("  {0,4}" -f $fg.Count) -ForegroundColor $color -NoNewline
  Write-Host "  $(Pad $fg.Name 40)" -NoNewline
  Write-Host "  $ruleBreakdown" -ForegroundColor DarkGray
}

Write-Section "LINES OF CODE BY LAYER"

$layers = $fileLoc | Group-Object Folder | ForEach-Object {
  [PSCustomObject]@{
    Layer = $_.Name
    Files = $_.Count
    Lines = ($_.Group | Measure-Object Lines -Sum).Sum
  }
} | Sort-Object Lines -Descending

foreach ($l in $layers) {
  $color = if ($l.Lines -ge 3000) { "Yellow" } else { "White" }
  Write-Host ("  {0,6}" -f $l.Lines) -ForegroundColor $color -NoNewline
  Write-Host " lines  " -ForegroundColor DarkGray -NoNewline
  Write-Host ("{0,3}" -f $l.Files) -NoNewline
  Write-Host " files  " -ForegroundColor DarkGray -NoNewline
  Write-Host $l.Layer
}

Write-Host "`n  Run with -ShowAll for all entries, -TopN 50 for more, -JsonOutput for JSON" -ForegroundColor DarkGray
Write-Host "  Run with -NoBuild to reuse cached output`n" -ForegroundColor DarkGray
