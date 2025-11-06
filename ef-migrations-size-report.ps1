# Base path: src folder
$srcPath = Join-Path -Path . -ChildPath 'src'

if (-not (Test-Path $srcPath)) {
    Write-Error "src folder not found in current directory!"
    exit 1
}

# Get all .cs files in src and subdirectories
$allCsFiles = Get-ChildItem -Path $srcPath -Filter *.cs -File -Recurse

# Filter files that are inside any folder named exactly "Migrations" (case-insensitive)
$migrationCsFiles = $allCsFiles | Where-Object {
    $_.Directory.Name -ieq "Migrations"
}

# Sizes in KB
$totalSize = [math]::Round(($allCsFiles | Measure-Object -Property Length -Sum).Sum / 1KB, 2)
$migrationSize = [math]::Round(($migrationCsFiles | Measure-Object -Property Length -Sum).Sum / 1KB, 2)

# Counts
$totalCount = $allCsFiles.Count
$migrationCount = $migrationCsFiles.Count

# Percentages
$sizePercent = if ($totalSize -gt 0) { [math]::Round(($migrationSize / $totalSize) * 100, 1) } else { 0 }
$countPercent = if ($totalCount -gt 0) { [math]::Round(($migrationCount / $totalCount) * 100, 1) } else { 0 }

# Output
"File size:  $($totalSize) KB, where $($migrationSize) KB ($sizePercent %) in Migrations folders"
"File count: $($totalCount), where $($migrationCount) ($countPercent %) in Migrations folders"