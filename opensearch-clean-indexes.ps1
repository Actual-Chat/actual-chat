#!/usr/bin/env pwsh
# Cross-platform script to clean OpenSearch indices

$OpenSearchUrl = "http://localhost:9200"

Write-Host "Checking OpenSearch connectivity..."
try {
    $null = Invoke-RestMethod -Uri "$OpenSearchUrl/_cluster/health" -TimeoutSec 5
} catch {
    Write-Host "ERROR: OpenSearch is not running at $OpenSearchUrl" -ForegroundColor Red
    Write-Host "Please start it with: docker-start.cmd"
    exit 1
}

# Get current indices
Write-Host ""
Write-Host "Current indices:"
try {
    $indices = Invoke-RestMethod -Uri "$OpenSearchUrl/_cat/indices?h=index&format=json" -TimeoutSec 10
    Write-Host "  Total: $($indices.Count) indices"

    $testIndices = $indices | Where-Object { $_.index -like "test-sm-*" }
    $devIndices = $indices | Where-Object { $_.index -like "dev-sm-*" }
    Write-Host "  Test indices (test-sm-*): $($testIndices.Count)"
    Write-Host "  Dev indices (dev-sm-*): $($devIndices.Count)"
} catch {
    Write-Host "  Could not retrieve index list" -ForegroundColor Yellow
}

# Delete test indices
Write-Host ""
Write-Host "Deleting test indices (test-sm-*)..."
try {
    $result = Invoke-RestMethod -Uri "$OpenSearchUrl/test-sm-*" -Method Delete -TimeoutSec 30
    Write-Host "  Test indices deleted." -ForegroundColor Green
} catch {
    if ($_.Exception.Response.StatusCode -eq 404) {
        Write-Host "  No test indices found." -ForegroundColor Yellow
    } else {
        Write-Host "  Error: $_" -ForegroundColor Red
    }
}

# Delete dev indices
Write-Host ""
Write-Host "Deleting dev indices (dev-sm-*)..."
try {
    $result = Invoke-RestMethod -Uri "$OpenSearchUrl/dev-sm-*" -Method Delete -TimeoutSec 30
    Write-Host "  Dev indices deleted." -ForegroundColor Green
} catch {
    if ($_.Exception.Response.StatusCode -eq 404) {
        Write-Host "  No dev indices found." -ForegroundColor Yellow
    } else {
        Write-Host "  Error: $_" -ForegroundColor Red
    }
}

# Show remaining indices
Write-Host ""
Write-Host "Remaining indices:"
try {
    $indices = Invoke-RestMethod -Uri "$OpenSearchUrl/_cat/indices?h=index&format=json" -TimeoutSec 10
    Write-Host "  Total: $($indices.Count) indices"
    if ($indices.Count -gt 0 -and $indices.Count -le 10) {
        foreach ($idx in $indices) {
            Write-Host "    - $($idx.index)"
        }
    }
} catch {
    Write-Host "  Could not retrieve index list" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "OpenSearch cleanup completed." -ForegroundColor Green
