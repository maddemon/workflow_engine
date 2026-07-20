$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { $PSScriptRoot } else { $PWD.Path }
$resultsDir = Join-Path $root 'TestResults'
$files = Get-ChildItem -Path $resultsDir -Filter 'coverage.cobertura.xml' -Recurse

if ($files.Count -eq 0) {
    throw "No coverage.cobertura.xml files found under $resultsDir. Run 'dotnet test --collect:""XPlat Code Coverage""' first."
}

function Get-BranchCounts($lineEl) {
    $cc = $lineEl.'condition-coverage'
    if (-not $cc) { return @(0, 0) }
    $m = [regex]::Match($cc, '\((\d+)/(\d+)\)')
    if (-not $m.Success) { return @(0, 0) }
    return @([int]$m.Groups[1].Value, [int]$m.Groups[2].Value)
}

# Per-package best line-rate (avoid double-counting across test projects)
$best = @{}
foreach ($f in $files) {
    [xml]$x = Get-Content $f.FullName
    foreach ($p in $x.coverage.packages.package) {
        $name = $p.name -replace 'FlowEngine\.', ''
        $cov = 0
        $tot = 0
        $bcov = 0
        $btot = 0
        foreach ($c in $p.classes.class) {
            foreach ($m in $c.methods.method) {
                foreach ($l in $m.lines.line) {
                    $tot++
                    if ([int]$l.hits -gt 0) { $cov++ }
                    if ($l.branch -eq 'True') {
                        $b = Get-BranchCounts $l
                        $bcov += $b[0]
                        $btot += $b[1]
                    }
                }
            }
        }

        if ($tot -eq 0 -and $btot -eq 0) { continue }

        $lr = if ($tot -gt 0) { $cov / $tot } else { 0 }
        $br = if ($btot -gt 0) { $bcov / $btot } else { 0 }

        if (-not $best.ContainsKey($name) -or $lr -gt $best[$name].lr) {
            $best[$name] = @{
                lr = $lr
                br = $br
                lines = $tot
                covered = $cov
                branches = $btot
                bcovered = $bcov
            }
        }
    }
}

$totCovered = 0
$totLines = 0
$totBCovered = 0
$totBTotal = 0

Write-Host "=== Backend coverage by package (best line-rate across test projects) ==="
foreach ($k in ($best.Keys | Sort-Object)) {
    $b = $best[$k]
    $totCovered += $b.covered
    $totLines += $b.lines
    $totBCovered += $b.bcovered
    $totBTotal += $b.branches
    Write-Host ("{0,-25} line {1,6:P1} ({2,6}/{3,6})  branch {4,6:P1} ({5,6}/{6,6})" -f $k, $b.lr, $b.covered, $b.lines, $b.br, $b.bcovered, $b.branches)
}

$overallLine = if ($totLines -gt 0) { $totCovered / $totLines } else { 0 }
$overallBranch = if ($totBTotal -gt 0) { $totBCovered / $totBTotal } else { 0 }

Write-Host "==="
Write-Host ("Backend overall line   {0:P1} ({1}/{2})" -f $overallLine, $totCovered, $totLines)
Write-Host ("Backend overall branch {0:P1} ({1}/{2})" -f $overallBranch, $totBCovered, $totBTotal)

# CI gates
$minLine = 0.75
$minBranch = 0.55

$failed = $false
if ($overallLine -lt $minLine) {
    Write-Error ("BACKEND LINE COVERAGE GATE FAILED: {0:P1} < {1:P1}" -f $overallLine, $minLine)
    $failed = $true
} else {
    Write-Host ("BACKEND LINE COVERAGE GATE PASSED: {0:P1} >= {1:P1}" -f $overallLine, $minLine) -ForegroundColor Green
}

if ($overallBranch -lt $minBranch) {
    Write-Error ("BACKEND BRANCH COVERAGE GATE FAILED: {0:P1} < {1:P1}" -f $overallBranch, $minBranch)
    $failed = $true
} else {
    Write-Host ("BACKEND BRANCH COVERAGE GATE PASSED: {0:P1} >= {1:P1}" -f $overallBranch, $minBranch) -ForegroundColor Green
}

if ($failed) { exit 1 }
