param(
    [Parameter(Mandatory)]
    [string] $SamplesPath,
    [string] $PostgresContainer = 'actual-chat-infra-postgres-1',
    [ValidateRange(1, 100000)]
    [int] $Rows = 5000,
    [switch] $WithDailyRow
)

$ErrorActionPreference = 'Stop'
$db = 'ac_pace_bench_' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$created = $false

function Invoke-Sql([string] $Database, [string] $Sql) {
    $result = & docker exec $PostgresContainer psql -X -q -v ON_ERROR_STOP=1 -U postgres -d $Database -At -c $Sql
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL benchmark command failed in $Database."
    }
    return ($result -join "`n")
}

try {
    $null = Invoke-Sql 'postgres' "CREATE DATABASE $db"
    $created = $true
    $samples = Get-Content -Raw $SamplesPath | ConvertFrom-Json
    foreach ($sample in $samples) {
        $segments = [int] $sample.segments
        if ($segments -lt 0) {
            throw 'Invalid sample segment count.'
        }
        foreach ($format in $sample.formats) {
            $name = [string] $format.format
            if ($name -notin @('json', 'messagePack', 'lz4', 'packed', 'none') -or
                ($name -eq 'none' -and -not $WithDailyRow)) {
                throw 'Unknown benchmark format.'
            }
            $base64 = [string] $format.base64
            if ($base64 -notmatch '^[A-Za-z0-9+/]*={0,2}$') {
                throw 'Invalid benchmark payload.'
            }
            $table = "pace_$($name.ToLowerInvariant())_$segments"
            $type = if ($name -eq 'json') { 'jsonb' } else { 'bytea' }
            $payload = if ($name -eq 'none') { 'NULL::bytea' } else { "decode('$base64', 'base64')" }
            if ($name -eq 'json') {
                $payload = "convert_from($payload, 'UTF8')::jsonb"
            }
            if ($WithDailyRow) {
                if ($type -ne 'bytea') {
                    throw 'Daily-row experiments require a binary payload.'
                }
                $dayBase64 = [string] $sample.dayBase64
                if ($dayBase64 -notmatch '^[A-Za-z0-9+/]+={0,2}$') {
                    throw 'Invalid daily JSON payload.'
                }
                $null = Invoke-Sql $db @"
CREATE TABLE $table (user_id text NOT NULL, language text NOT NULL, day timestamp NOT NULL,
    version bigint NOT NULL, data jsonb NOT NULL, payload bytea,
    PRIMARY KEY (user_id, language, day))
"@
                $dayData = "convert_from(decode('$dayBase64', 'base64'), 'UTF8')::jsonb"
                $insert = "INSERT INTO $table SELECT 'benchmark-' || id, 'en', timestamp '2026-10-08', 1, " +
                    "$dayData, $payload FROM generate_series(1, $Rows) AS id"
            }
            else {
                $null = Invoke-Sql $db "CREATE TABLE $table (id bigint PRIMARY KEY, payload $type NOT NULL)"
                $insert = "INSERT INTO $table SELECT id, $payload FROM generate_series(1, $Rows) AS id"
            }
            $explain = Invoke-Sql $db @"
EXPLAIN (ANALYZE, WAL, BUFFERS, FORMAT JSON)
$insert
"@ | ConvertFrom-Json
            $stats = Invoke-Sql $db @"
SELECT json_build_object(
    'columnBytes', (SELECT avg(pg_column_size(payload)) FROM $table),
    'heapBytes', pg_relation_size('$table'),
    'toastAndAuxiliaryBytes', pg_table_size('$table') - pg_relation_size('$table'),
    'indexBytes', pg_indexes_size('$table'),
    'totalBytes', pg_total_relation_size('$table'))
"@ | ConvertFrom-Json
            $update = Invoke-Sql $db @"
EXPLAIN (ANALYZE, WAL, BUFFERS, FORMAT JSON)
UPDATE $table SET payload = $payload
"@ | ConvertFrom-Json
            $afterUpdate = Invoke-Sql $db "SELECT pg_total_relation_size('$table')"
            $result = [ordered] @{
                segments = $segments
                dailyRow = [bool] $WithDailyRow
                format = $name
                rows = $Rows
                columnBytes = $stats.columnBytes
                heapBytes = $stats.heapBytes
                toastAndAuxiliaryBytes = $stats.toastAndAuxiliaryBytes
                indexBytes = $stats.indexBytes
                totalBytes = $stats.totalBytes
                allocatedBytesPerRow = $stats.totalBytes / $Rows
                insertWalBytes = $explain[0].Plan.'WAL Bytes'
                insertWalBytesPerRow = $explain[0].Plan.'WAL Bytes' / $Rows
                insertMilliseconds = $explain[0].'Execution Time'
                updateWalBytes = $update[0].Plan.'WAL Bytes'
                updateWalBytesPerRow = $update[0].Plan.'WAL Bytes' / $Rows
                updateMilliseconds = $update[0].'Execution Time'
                allocatedBytesPerRowAfterUpdate = [long] $afterUpdate / $Rows
            }
            $result | ConvertTo-Json -Compress
        }
    }
}
finally {
    if ($created) {
        $null = Invoke-Sql 'postgres' "DROP DATABASE $db WITH (FORCE)"
    }
}
