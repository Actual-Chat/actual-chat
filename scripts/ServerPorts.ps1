param(
    [string]$ProjectPath,
    [string]$CustomPort = "",
    [switch]$Kill,
    [Alias("all")][switch]$CheckAll
)

. "$ProjectPath/scripts/Common.ps1"

$envFile = Join-Path $ProjectPath '.env'
$instance = 'dev'; $port = 7080
if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        if ($_ -match '^urls=.*?(\d+)$') { $port = [int]$Matches[1] }
        if ($_ -match '^HostSettings__BasePort=(\d+)$') { $port = [int]$Matches[1] }
        if ($_ -match '^CoreSettings__Instance=(.+)$') { $instance = $Matches[1] }
    }
}

if ($CustomPort) { $port = [int]$CustomPort }
$os = Get-CurrentOS
$onWindows = $os -eq 'Windows'
$inDocker = $os -eq 'Docker'
$pidFile = Join-Path $ProjectPath 'tmp' "server-$instance.pid"

function Test-PortListening([int]$p) {
    # /proc/net/tcp: fastest on Linux/Docker/WSL
    if (-not $onWindows) {
        $hexPort = '{0:X4}' -f $p
        foreach ($f in @('/proc/net/tcp', '/proc/net/tcp6')) {
            if (Test-Path $f) {
                $lines = Get-Content $f | Where-Object { $_ -match ":$hexPort\s" -and $_ -match '\s0A\s' }
                if ($lines) { return $true }
            }
        }
    }
    # TcpClient: cross-platform fallback (and primary for Windows)
    try {
        $c = [System.Net.Sockets.TcpClient]::new()
        $c.Connect('127.0.0.1', $p)
        $c.Dispose()
        return $true
    } catch {
        return $false
    }
}

function Find-ListeningPid([int]$p) {
    if ($onWindows) {
        # Get-NetTCPConnection is the native PowerShell way on Windows
        try {
            $conn = Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction Stop | Select-Object -First 1
            if ($conn) {
                $proc = Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
                return @{
                    Pid = $conn.OwningProcess
                    Name = if ($proc) { $proc.ProcessName } else { '' }
                    User = ''
                    Source = 'NetTCPConnection'
                }
            }
        } catch {}
        return $null
    }

    # Linux/Docker/WSL/macOS: try available tools in order
    $lsof = bash -c "lsof -i :$p 2>/dev/null | grep LISTEN | head -1" 2>$null
    if ($lsof) {
        $parts = $lsof -split '\s+'
        return @{ Pid = [int]$parts[1]; Name = $parts[0]; User = $parts[2]; Source = 'lsof' }
    }
    $ss = bash -c "ss -tlnp 2>/dev/null | grep ':$p '" 2>$null
    if ($ss -and $ss -match 'pid=(\d+)') {
        return @{ Pid = [int]$Matches[1]; Name = ''; User = ''; Source = 'ss' }
    }
    $fuser = bash -c "fuser $p/tcp 2>/dev/null" 2>$null
    if ($fuser -and $fuser -match '(\d+)') {
        return @{ Pid = [int]$Matches[1]; Name = ''; User = ''; Source = 'fuser' }
    }
    return $null
}

function Get-ProcessDetails([int]$procId) {
    $details = @{}

    if ($onWindows) {
        $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
        if ($proc) {
            $details['Command'] = $proc.Path
            $details['Started'] = $proc.StartTime.ToString('yyyy-MM-dd HH:mm:ss')

            # Get parent PID via CIM
            $wmiProc = Get-CimInstance Win32_Process -Filter "ProcessId = $procId" -ErrorAction SilentlyContinue
            if ($wmiProc -and $wmiProc.ParentProcessId) {
                $parentProc = Get-Process -Id $wmiProc.ParentProcessId -ErrorAction SilentlyContinue
                if ($parentProc) {
                    $details['Parent'] = "PID $($wmiProc.ParentProcessId) ($($parentProc.ProcessName))"
                }
            }

            # Child processes via CIM
            $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $procId" -ErrorAction SilentlyContinue)
            if ($children.Count -gt 0) { $details['Children'] = "$($children.Count) child process(es)" }
        }
        return $details
    }

    # Linux/Docker/WSL/macOS
    $cmdline = bash -c "cat /proc/$procId/cmdline 2>/dev/null | tr '\0' ' '" 2>$null
    if ($cmdline) { $details['Command'] = $cmdline.Trim() }
    $startTime = bash -c "stat -c '%y' /proc/$procId 2>/dev/null | cut -d. -f1" 2>$null
    if ($startTime) { $details['Started'] = $startTime.Trim() }
    $ppid = $null
    $statusLines = bash -c "cat /proc/$procId/status 2>/dev/null" 2>$null
    if ($statusLines) {
        foreach ($line in ($statusLines -split "`n")) {
            if ($line -match '^PPid:\s+(\d+)') { $ppid = [int]$Matches[1] }
        }
    }
    if ($ppid) {
        $parentCmd = bash -c "cat /proc/$ppid/cmdline 2>/dev/null | tr '\0' ' '" 2>$null
        if ($parentCmd) { $details['Parent'] = "PID $ppid ($($parentCmd.Trim()))" }
    }
    $children = @(bash -c "pgrep -P $procId 2>/dev/null" 2>$null | Where-Object { $_ })
    if ($children.Count -gt 0) { $details['Children'] = "$($children.Count) child process(es)" }
    return $details
}

function Kill-ProcessTree([int]$procId) {
    if ($onWindows) {
        # taskkill /T kills the process tree natively on Windows
        taskkill /PID $procId /T /F 2>$null | Out-Null
    } else {
        # Recursive kill on Linux/macOS
        $ch = bash -c "pgrep -P $procId 2>/dev/null" 2>$null
        if ($ch) {
            foreach ($c in ($ch -split "`n" | Where-Object { $_ })) { Kill-ProcessTree([int]$c) }
        }
        try {
            $p = [System.Diagnostics.Process]::GetProcessById($procId)
            if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit(3000) | Out-Null }
        } catch {}
    }
}

function Check-Port([int]$checkPort, [string]$label) {
    Write-Host ''
    Write-Host "=== Port $checkPort ($label) ==="

    if (-not (Test-PortListening $checkPort)) {
        Write-Host '  Status: FREE - port is available'
        return
    }
    Write-Host '  Status: IN USE'

    $procInfo = Find-ListeningPid $checkPort
    if (-not $procInfo) {
        Write-Host '  Process: NOT VISIBLE from this environment'
        if ($inDocker) {
            Write-Host ''
            Write-Host '  The port is held by a HOST process (not visible from Docker).'
            Write-Host '  Run on the HOST to identify and kill it:'
            Write-Host ''
            Write-Host "    # macOS / Linux"
            Write-Host "    lsof -i :$checkPort"
            Write-Host "    kill <pid>"
            Write-Host ''
            Write-Host '    # Windows (PowerShell)'
            Write-Host "    Get-NetTCPConnection -LocalPort $checkPort -State Listen"
            Write-Host '    Stop-Process -Id <pid> -Force'
        } else {
            Write-Host '  Could not identify the process. Try running with elevated privileges:'
            Write-Host "    sudo lsof -i :$checkPort"
        }
        return
    }

    $foundPid = $procInfo.Pid
    $procName = $procInfo.Name
    if ($procName) {
        Write-Host "  Process: $procName (PID: $foundPid$(if ($procInfo.User) { ", user: $($procInfo.User)" }))"
    } else {
        Write-Host "  Process: PID $foundPid (found via $($procInfo.Source))"
    }

    $details = Get-ProcessDetails $foundPid
    if ($details['Command'])  { Write-Host "  Command: $($details['Command'])" }
    if ($details['Started'])  { Write-Host "  Started: $($details['Started'])" }
    if ($details['Parent'])   { Write-Host "  Parent:  $($details['Parent'])" }
    if ($details['Children']) { Write-Host "  Children: $($details['Children'])" }

    $knownPid = $null
    if ($checkPort -eq $port -and (Test-Path $pidFile)) {
        try { $knownPid = [int](Get-Content $pidFile -Raw).Trim() } catch {}
    }
    if ($knownPid) {
        if ($knownPid -eq $foundPid) {
            Write-Host '  Identity: KNOWN server (matches PID file)'
        } else {
            Write-Host "  Identity: ORPHAN - PID file says $knownPid, but port held by $foundPid"
        }
    } else {
        Write-Host '  Identity: UNKNOWN - no PID file for this instance'
    }

    if ($Kill) {
        Write-Host ''
        Write-Host "  Killing process tree (PID: $foundPid)..."
        Kill-ProcessTree $foundPid
        Remove-Item $pidFile -ErrorAction SilentlyContinue

        Start-Sleep -Milliseconds 500
        if (Test-PortListening $checkPort) {
            Write-Host '  Result: Port still in use (process may need more time to exit)'
        } else {
            Write-Host '  Result: Port is now FREE'
        }
    } else {
        Write-Host ''
        Write-Host '  To kill this process, run:'
        Write-Host "    /server-port-check --kill"
    }
}

if ($CheckAll) {
    # Find main project path: in a worktree, git-common-dir points to <main>/.git
    $mainPath = $ProjectPath
    $gitCommonDir = git -C $ProjectPath rev-parse --git-common-dir 2>$null
    if ($gitCommonDir -and $gitCommonDir -ne '.git') {
        $resolved = [System.IO.Path]::GetFullPath($gitCommonDir)
        $mainPath = Split-Path -Parent $resolved
    }

    $portsFile = Join-Path $mainPath 'artifacts' 'server-ports.json'
    if (Test-Path $portsFile) {
        $registry = Get-Content $portsFile -Raw | ConvertFrom-Json -AsHashtable
        Write-Host "Checking $($registry.Count) registered worktree port(s)..."
        foreach ($entry in $registry.GetEnumerator() | Sort-Object Value) {
            Check-Port $entry.Value $entry.Key
        }
    } else {
        Write-Host "No port registry found ($portsFile)"
        Check-Port $port $instance
    }
} else {
    Write-Host "Checking port for instance: $instance"
    Check-Port $port $instance
}

Write-Host ''
