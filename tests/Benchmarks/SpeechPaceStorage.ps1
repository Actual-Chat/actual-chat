param(
    [Parameter(Mandatory)]
    [string] $SamplesPath,
    [string] $PostgresContainer = 'actual-chat-infra-postgres-1',
    [ValidateRange(1, 100000)]
    [int] $Rows = 5000
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
            if ($name -notin @('json', 'messagePack', 'lz4', 'packed')) {
                throw 'Unknown benchmark format.'
            }
            $base64 = [string] $format.base64
            if ($base64 -notmatch '^[A-Za-z0-9+/]*={0,2}$') {
                throw 'Invalid benchmark payload.'
            }
            $table = "pace_$($name.ToLowerInvariant())_$segments"
            $type = if ($name -eq 'json') { 'jsonb' } else { 'bytea' }
            $payload = "decode('$base64', 'base64')"
            if ($name -eq 'json') {
                $payload = "convert_from($payload, 'UTF8')::jsonb"
            }
            $null = Invoke-Sql $db "CREATE TABLE $table (id bigint PRIMARY KEY, payload $type NOT NULL)"
            $explain = Invoke-Sql $db @"
EXPLAIN (ANALYZE, WAL, BUFFERS, FORMAT JSON)
INSERT INTO $table SELECT id, $payload FROM generate_series(1, $Rows) AS id
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
