# Common PowerShell utilities for ActualChat scripts

function Get-CurrentOS {
    <#
    .SYNOPSIS
        Detects the current operating system/environment.
    .OUTPUTS
        One of: Windows, Docker, WSL, Linux, macOS, Unknown
    #>
    if ($IsWindows -or $env:OS -eq "Windows_NT") {
        return "Windows"
    } elseif ($IsLinux) {
        # Check if running in Docker
        if ((Test-Path "/.dockerenv") -or ((Test-Path "/proc/1/cgroup") -and (Get-Content "/proc/1/cgroup" | Select-String -Pattern "docker|kubepods" -Quiet))) {
            return "Docker"
        }
        # Check if running in WSL
        if (Test-Path "/proc/version") {
            $version = Get-Content "/proc/version"
            if ($version -match "microsoft|WSL") {
                return "WSL"
            }
        }
        return "Linux"
    } elseif ($IsMacOS) {
        return "macOS"
    }
    return "Unknown"
}

function Get-LocalIP {
    <#
    .SYNOPSIS
        Detects the local LAN IP address (first non-localhost IPv4).
    #>
    $os = Get-CurrentOS
    $localIp = $null
    switch ($os) {
        "macOS" {
            $localIp = (ifconfig | Select-String 'inet (\d+\.\d+\.\d+\.\d+)' -AllMatches).Matches |
                ForEach-Object { $_.Groups[1].Value } |
                Where-Object { $_ -ne '127.0.0.1' } |
                Select-Object -First 1
        }
        "Windows" {
            $localIp = (Get-NetIPAddress -AddressFamily IPv4 |
                Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.PrefixOrigin -ne 'WellKnown' } |
                Select-Object -First 1).IPAddress
        }
        default {
            # Linux, Docker, WSL
            $localIp = (hostname -I 2>$null) -split ' ' | Where-Object { $_ } | Select-Object -First 1
            if (-not $localIp) {
                $localIp = (ip route get 1.1.1.1 2>$null | Select-String 'src (\d+\.\d+\.\d+\.\d+)').Matches.Groups[1].Value
            }
        }
    }
    return $localIp
}

function Set-EnvFileValue {
    <#
    .SYNOPSIS
        Sets a key=value in a .env file. Creates the file if it doesn't exist.
    .PARAMETER Path
        Path to the .env file.
    .PARAMETER Key
        The environment variable name.
    .PARAMETER Value
        The value to set.
    #>
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Key,
        [Parameter(Mandatory)][string]$Value
    )

    $content = if (Test-Path $Path) { Get-Content $Path -Raw } else { "" }
    if (-not $content) { $content = "" }

    if ($content -match "(?m)^$Key=") {
        $content = $content -replace "(?m)^$Key=.*$", "$Key=$Value"
    } else {
        $content = $content.TrimEnd() + "`n$Key=$Value`n"
    }

    Set-Content $Path $content.Trim() -NoNewline
    Add-Content $Path ""  # ensure trailing newline
}

function Update-LocalIP {
    <#
    .SYNOPSIS
        Detects local IP and writes it to .env file.
    .PARAMETER EnvFile
        Path to .env file. Defaults to .env in current directory.
    .OUTPUTS
        The detected local IP address.
    #>
    param(
        [string]$EnvFile = (Join-Path (Get-Location).Path ".env")
    )

    $localIp = Get-LocalIP
    if (-not $localIp) {
        Write-Error "Could not detect local IP address"
        return $null
    }

    Set-EnvFileValue -Path $EnvFile -Key "LOCAL_IP" -Value $localIp
    Write-Host "Updated $EnvFile with LOCAL_IP=$localIp"
    return $localIp
}

function Get-HostsFilePath {
    <#
    .SYNOPSIS
        Returns the platform-specific hosts file path.
    #>
    if ((Get-CurrentOS) -eq "Windows") {
        return "$env:SystemRoot\System32\drivers\etc\hosts"
    } else {
        return "/etc/hosts"
    }
}

function Update-HostEntries {
    <#
    .SYNOPSIS
        Adds or updates entries in the hosts file.
    .PARAMETER Hostnames
        Array of hostnames to add/update.
    .PARAMETER IP
        The IP address for the hosts entries.
    .PARAMETER DetectIP
        If set, detects LAN IP automatically instead of using -IP.
    #>
    param(
        [Parameter(Mandatory)][string[]]$Hostnames,
        [string]$IP,
        [switch]$DetectIP
    )

    if ($DetectIP) {
        $IP = Get-LocalIP
        if (-not $IP) {
            Write-Error "Could not detect local IP address"
            return $null
        }
    } elseif (-not $IP) {
        $IP = "127.0.0.1"
    }

    $hostsFile = Get-HostsFilePath
    $hostsContent = Get-Content $hostsFile -ErrorAction SilentlyContinue
    if (-not $hostsContent) { $hostsContent = @() }

    $entriesToAdd = @()
    $needsUpdate = $false

    foreach ($hostname in $Hostnames) {
        $escapedHostname = [regex]::Escape($hostname)
        $existingLine = $hostsContent | Where-Object { $_ -match "(?<=\s)$escapedHostname(?=\s|$)" }
        $newLine = "$IP  $hostname"

        if ($existingLine) {
            if ($existingLine -notmatch "^\s*$([regex]::Escape($IP))\s+") {
                $needsUpdate = $true
                $entriesToAdd += $newLine
            }
        } else {
            $entriesToAdd += $newLine
        }
    }

    if ($entriesToAdd.Count -eq 0) {
        Write-Host "Hosts entries already up-to-date"
        return $IP
    }

    # Build the update script
    $script = ""
    if ($needsUpdate) {
        $patterns = ($Hostnames | ForEach-Object { [regex]::Escape($_) }) -join '|'
        $script += "`$content = Get-Content '$hostsFile' | Where-Object { `$_ -notmatch '(?<=\s)($patterns)(?=\s|$)' }; "
        $script += "Set-Content '$hostsFile' `$content -Force; "
    }
    $newEntries = $entriesToAdd -join "`n"
    $script += "Add-Content '$hostsFile' `"``n$newEntries`""

    if ((Get-CurrentOS) -eq "Windows") {
        try {
            Invoke-Expression $script
            Write-Host "Updated hosts file"
        } catch {
            Write-Host "Requesting elevation to update hosts file..."
            try {
                Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -Command $script" -Wait -ErrorAction Stop
                Write-Host "Updated hosts file (via UAC)"
            } catch {
                Write-Host "Could not update hosts file. Add manually:" -ForegroundColor Yellow
                $entriesToAdd | ForEach-Object { Write-Host $_ }
            }
        }
    } else {
        Write-Host "Updating hosts file (sudo required)..."
        $tempFile = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "update-hosts-$(Get-Random).ps1")
        try {
            Set-Content $tempFile $script -NoNewline
            sudo pwsh -NoProfile -File $tempFile
            if ($LASTEXITCODE -eq 0) {
                Write-Host "Updated hosts file"
            } else {
                Write-Host "Could not update hosts file. Add manually:" -ForegroundColor Yellow
                $entriesToAdd | ForEach-Object { Write-Host $_ }
            }
        } finally {
            Remove-Item $tempFile -ErrorAction SilentlyContinue
        }
    }

    return $IP
}

function Remove-HostEntries {
    <#
    .SYNOPSIS
        Removes entries from the hosts file by hostname.
    .PARAMETER Hostnames
        Array of hostnames to remove.
    #>
    param(
        [Parameter(Mandatory)][string[]]$Hostnames
    )

    $hostsFile = Get-HostsFilePath
    $hostsContent = Get-Content $hostsFile -ErrorAction SilentlyContinue
    if (-not $hostsContent) { return }

    $newContent = $hostsContent | Where-Object {
        $line = $_
        $shouldKeep = $true
        foreach ($hostname in $Hostnames) {
            if ($line -match "(?<=\s)$([regex]::Escape($hostname))(?=\s|$)") {
                $shouldKeep = $false
                break
            }
        }
        $shouldKeep
    }

    if ($newContent.Count -eq $hostsContent.Count) {
        Write-Host "No matching hosts entries found"
        return
    }

    if ((Get-CurrentOS) -eq "Windows") {
        try {
            Set-Content -Path $hostsFile -Value $newContent -ErrorAction Stop
            Write-Host "Removed hosts entries"
        } catch {
            Write-Host "Requesting elevation to update hosts file..."
            try {
                $tempFile = [System.IO.Path]::GetTempFileName()
                $newContent | Set-Content -Path $tempFile
                $script = "Copy-Item '$tempFile' '$hostsFile' -Force; Remove-Item '$tempFile'"
                Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -Command $script" -Wait -ErrorAction Stop
                Write-Host "Removed hosts entries (via UAC)"
            } catch {
                Write-Host "Could not update hosts file" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "Removing hosts entries (sudo required)..."
        $tempFile = [System.IO.Path]::GetTempFileName()
        $newContent | Set-Content -Path $tempFile
        bash -c "sudo cp '$tempFile' '$hostsFile' && rm '$tempFile'"
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Removed hosts entries"
        } else {
            Write-Host "Could not update hosts file" -ForegroundColor Yellow
            Remove-Item $tempFile -ErrorAction SilentlyContinue
        }
    }
}

function Get-ScriptDirectory {
    <#
    .SYNOPSIS
        Gets the directory containing the calling script.
    #>
    $dir = $PSScriptRoot
    if (-not $dir -and $PSCommandPath) { $dir = Split-Path -Parent $PSCommandPath }
    if (-not $dir) { $dir = (Get-Location).Path }
    return $dir
}

# --- Server management ---

# Base type for server control. Returned by [AppServerFactory]::Connect().
# PowerShell has no abstract methods — these throw to enforce override.
class AppServer {
    [hashtable] GetStatus()                          { throw "Not implemented" }
    [hashtable] Stop()                               { throw "Not implemented" }
    [hashtable] Start([bool]$watch)                  { throw "Not implemented" }
    [hashtable] Restart([bool]$watch, [bool]$noBuild) { throw "Not implemented" }
    [hashtable] Build()                              { throw "Not implemented" }
    [hashtable] GetLog([int]$lines)                  { throw "Not implemented" }
}

class LocalAppServer : AppServer {
    [string]$ProjectPath
    [string]$Instance
    [string]$BaseUri
    [int]$Port
    [string]$LogFile
    [string]$ErrFile
    [string]$PidFile
    [string]$ServerProject
    [System.Diagnostics.Process]$Process = $null

    LocalAppServer([string]$projectPath) {
        $this.ProjectPath = $projectPath
        $this.Instance = "dev"
        $this.BaseUri = "https://local.voxt.ai"
        $this.Port = 7080

        $envFile = Join-Path $projectPath ".env"
        if (Test-Path $envFile) {
            Get-Content $envFile | ForEach-Object {
                if ($_ -match "^urls=.*?(\d+)$") { $this.Port = [int]$Matches[1] }
                if ($_ -match "^CoreSettings__Instance=(.+)$") { $this.Instance = $Matches[1] }
                if ($_ -match "^HostSettings__BaseUri=(.+)$") { $this.BaseUri = $Matches[1] }
            }
        }

        $tmpDir = Join-Path $projectPath "tmp"
        if (-not (Test-Path $tmpDir)) { New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null }
        $this.LogFile = Join-Path $tmpDir "server-$($this.Instance).log"
        $this.ErrFile = "$($this.LogFile).err"
        $this.PidFile = Join-Path $tmpDir "server-$($this.Instance).pid"
        $this.ServerProject = Join-Path $projectPath "src" "dotnet" "App.Server" "App.Server.csproj"
        $this.TryReconnect()
    }

    [void] TryReconnect() {
        # Try PID file first
        if (Test-Path $this.PidFile) {
            try {
                $savedPid = [int](Get-Content $this.PidFile -Raw).Trim()
                $proc = [System.Diagnostics.Process]::GetProcessById($savedPid)
                if (-not $proc.HasExited) {
                    $this.Process = $proc
                    return
                }
            } catch {}
            Remove-Item $this.PidFile -ErrorAction SilentlyContinue
        }

        # Fallback: find process listening on our port
        $lsofOutput = bash -c "lsof -i :$($this.Port) 2>/dev/null | grep LISTEN | head -1" 2>$null
        if ($lsofOutput) {
            try {
                $foundPid = [int](($lsofOutput -split '\s+')[1])
                $proc = [System.Diagnostics.Process]::GetProcessById($foundPid)
                if (-not $proc.HasExited) {
                    $this.Process = $proc
                    Set-Content $this.PidFile $foundPid
                }
            } catch {}
        }
    }

    [bool] IsRunning() {
        return $this.Process -and -not $this.Process.HasExited
    }

    [bool] IsPortInUse() {
        $check = bash -c "lsof -i :$($this.Port) 2>/dev/null | grep LISTEN" 2>$null
        return [bool]$check
    }

    [hashtable] GetStatus() {
        return @{
            status   = if ($this.IsRunning()) { "running" } else { "stopped" }
            instance = $this.Instance
            baseUri  = $this.BaseUri
            port     = $this.Port
            pid      = if ($this.IsRunning()) { $this.Process.Id } else { $null }
        }
    }

    [hashtable] Stop() {
        if (-not $this.IsRunning()) {
            return @{ stopped = $false; message = "No running server found" }
        }

        $procId = $this.Process.Id
        Write-Host "Stopping server $($this.Instance) (PID: $procId)..."
        # Use Kill($false) — Kill($true) uses process-group signaling on Linux,
        # which can kill the parent watch-agent process (same PGID).
        try { $this.Process.Kill() } catch {}
        Start-Sleep -Seconds 2
        try { if (-not $this.Process.HasExited) { $this.Process.Kill() } } catch {}
        $this.Process = $null
        Remove-Item $this.PidFile -ErrorAction SilentlyContinue
        return @{ stopped = $true; pid = $procId }
    }

    [hashtable] Start([bool]$watch) {
        if ($this.IsRunning()) {
            return @{ started = $false; message = "Already running"; pid = $this.Process.Id; port = $this.Port }
        }

        if ($this.IsPortInUse()) {
            return @{ started = $false; message = "Port $($this.Port) already in use" }
        }

        $env:ActualChat_CaptchaBypassEnabled = "true"
        $env:ASPNETCORE_ENVIRONMENT = "Development"

        $mode = if ($watch) { "watch mode" } else { "run" }
        Write-Host "Starting server: $($this.Instance) on port $($this.Port) ($mode)"

        $dotnetArgs = if ($watch) {
            @("watch", "run", "--project", $this.ServerProject, "--no-launch-profile")
        } else {
            @("run", "--project", $this.ServerProject, "--no-launch-profile")
        }

        $psi = [System.Diagnostics.ProcessStartInfo]::new("dotnet", ($dotnetArgs -join " "))
        $psi.WorkingDirectory = $this.ProjectPath
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $this.Process = [System.Diagnostics.Process]::Start($psi)
        Set-Content $this.PidFile $this.Process.Id
        # Drain stdout/stderr to log files via async stream copy.
        # NOTE: Do NOT use BeginOutputReadLine/BeginErrorReadLine — on Linux,
        # killing a child process with active async readers crashes the parent
        # PowerShell process with SIGABRT (exit code 134).
        $logFs = [System.IO.FileStream]::new($this.LogFile, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
        $errFs = [System.IO.FileStream]::new($this.ErrFile, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
        $null = $this.Process.StandardOutput.BaseStream.CopyToAsync($logFs).ContinueWith(
            [System.Action[System.Threading.Tasks.Task]]{ param($t) $logFs.Dispose() })
        $null = $this.Process.StandardError.BaseStream.CopyToAsync($errFs).ContinueWith(
            [System.Action[System.Threading.Tasks.Task]]{ param($t) $errFs.Dispose() })

        # Health check: poll until ready
        $ready = $false
        $readyTime = $null
        for ($i = 1; $i -le 90; $i++) {
            Start-Sleep -Seconds 1
            if ($this.Process.HasExited) {
                Write-Host "Server process exited unexpectedly"
                break
            }
            try {
                $null = Invoke-WebRequest -Uri "http://localhost:$($this.Port)" -TimeoutSec 2 -ErrorAction Stop
                $ready = $true
                $readyTime = $i
                Write-Host "Server ready! (${i}s)"
                break
            } catch {}
        }

        return @{
            started   = $true
            pid       = $this.Process.Id
            port      = $this.Port
            watch     = $watch
            ready     = $ready
            readyTime = $readyTime
        }
    }

    [hashtable] Build() {
        Write-Host "Building..."
        $output = & dotnet build $this.ServerProject --verbosity quiet 2>&1
        $result = @{ exitCode = $LASTEXITCODE; output = ($output | Out-String) }
        if ($LASTEXITCODE -eq 0) { Write-Host "Build complete" }
        return $result
    }

    [hashtable] Restart([bool]$watch, [bool]$noBuild) {
        $stopResult = $this.Stop()

        $buildResult = $null
        if (-not $noBuild) {
            $buildResult = $this.Build()
            if ($buildResult.exitCode -ne 0) {
                return @{ error = "Build failed"; stop = $stopResult; build = $buildResult }
            }
        }

        $startResult = $this.Start($watch)
        return @{ stop = $stopResult; build = $buildResult; start = $startResult }
    }

    [hashtable] GetLog([int]$lines) {
        $logContent = ""
        if (Test-Path $this.LogFile) {
            $logContent = Get-Content $this.LogFile -Tail $lines | Out-String
        }
        $errContent = ""
        if (Test-Path $this.ErrFile) {
            $errContent = Get-Content $this.ErrFile -Tail $lines | Out-String
        }
        return @{ log = $logContent; stderr = $errContent }
    }
}

# HTTP client for talking to a remote WatchAgent over HTTP.
# Same method signatures as LocalAppServer so callers don't need to know which they're using.
class RemoteAppServer : AppServer {
    [string]$BaseUrl

    RemoteAppServer([string]$baseUrl) {
        $this.BaseUrl = $baseUrl.TrimEnd('/')
    }

    # Create from AC_WATCH_AGENT_PORT env var. Returns $null if not set or unreachable.
    static [RemoteAppServer] TryCreate() {
        $port = $env:AC_WATCH_AGENT_PORT
        if (-not $port) { return $null }

        # Resolve host.docker.internal for Docker on macOS/Windows
        $hostIp = "localhost"
        try {
            $resolved = bash -c "getent ahosts host.docker.internal 2>/dev/null | grep -oP '^\d+\.\d+\.\d+\.\d+' | head -1" 2>$null
            if ($resolved) { $hostIp = $resolved.Trim() }
        } catch {}

        $url = "http://${hostIp}:$port"
        try {
            $null = Invoke-RestMethod -Uri "$url/health" -TimeoutSec 2 -ErrorAction Stop
            return [RemoteAppServer]::new($url)
        } catch {
            Write-Host "Warning: Watch agent not reachable at $url"
            return $null
        }
    }

    hidden [PSObject] Get([string]$path) {
        return Invoke-RestMethod -Uri "$($this.BaseUrl)$path" -TimeoutSec 120 -ErrorAction Stop
    }

    hidden [PSObject] Post([string]$path, [hashtable]$body) {
        $json = if ($body) { $body | ConvertTo-Json -Compress } else { "{}" }
        return Invoke-RestMethod -Uri "$($this.BaseUrl)$path" -Method Post `
            -Body $json -ContentType "application/json" -TimeoutSec 120 -ErrorAction Stop
    }

    [PSObject] GetStatus()                             { return $this.Get("/server/status") }
    [PSObject] Stop()                                  { return $this.Post("/server/stop", @{}) }
    [PSObject] Start([bool]$watch)                     { return $this.Post("/server/start", @{ watch = $watch }) }
    [PSObject] Restart([bool]$watch, [bool]$noBuild)   { return $this.Post("/server/restart", @{ watch = $watch; noBuild = $noBuild }) }
    [PSObject] Build()                                 { return $this.Post("/server/restart", @{ noBuild = $false }) }
    [PSObject] GetLog([int]$lines)                     { return $this.Get("/server/log?lines=$lines") }
}

# Factory that returns the right AppServer implementation.
# Returns RemoteAppServer if a watch-agent is available, otherwise LocalAppServer.
class AppServerFactory {
    static [AppServer] Create([string]$projectPath) {
        $remote = [RemoteAppServer]::TryCreate()
        if ($remote) {
            Write-Host "(via watch agent)"
            return $remote
        }
        return [LocalAppServer]::new($projectPath)
    }
}

class WatchAgent {
    [string]$ProjectPath
    [int]$Port
    [System.Diagnostics.Process]$Process = $null

    WatchAgent([string]$projectPath, [int]$port) {
        $this.ProjectPath = $projectPath
        $this.Port = $port
    }

    # Start the agent as a background process
    [void] Start() {
        $commonScript = Join-Path $this.ProjectPath "scripts" "Common.ps1"
        $cmd = ". '$commonScript'; [WatchAgent]::Run($($this.Port), '$($this.ProjectPath)')"

        # Write command to a temp script to avoid quoting issues
        $tmpDir = Join-Path $this.ProjectPath "tmp"
        if (-not (Test-Path $tmpDir)) { New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null }
        $scriptFile = Join-Path $tmpDir "watch-agent-run.ps1"
        Set-Content -Path $scriptFile -Value $cmd

        $onWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
        if ($onWindows) {
            $this.Process = Start-Process -FilePath "pwsh" -ArgumentList @(
                "-NoProfile", "-NonInteractive", "-File", $scriptFile
            ) -PassThru -WindowStyle Hidden
        } else {
            # Use .NET Process directly — Start-Process has permission issues with
            # redirects on Linux, and -WindowStyle is unsupported on non-Windows.
            $psi = [System.Diagnostics.ProcessStartInfo]::new("pwsh")
            $psi.Arguments = "-NoProfile -NonInteractive -File `"$scriptFile`""
            $psi.WorkingDirectory = $this.ProjectPath
            $psi.UseShellExecute = $false
            $psi.RedirectStandardOutput = $true
            $psi.RedirectStandardError = $true
            $this.Process = [System.Diagnostics.Process]::Start($psi)
            # Discard output to prevent blocking
            $null = $this.Process.StandardOutput.ReadLineAsync()
            $null = $this.Process.StandardError.ReadLineAsync()
        }
        Write-Host "Watch agent started on port $($this.Port) (PID: $($this.Process.Id))"
    }

    # Stop the background process
    [void] Stop() {
        if ($this.Process -and -not $this.Process.HasExited) {
            Write-Host "Stopping watch agent..."
            try { $this.Process.Kill($true) } catch {}
        }
        $this.Process = $null
    }

    # --- Static: the blocking HTTP server loop (runs in the background process) ---

    static [void] Run([int]$port, [string]$projectPath) {
        $server = [LocalAppServer]::new($projectPath)

        $listener = [System.Net.HttpListener]::new()
        $listener.Prefixes.Add("http://+:$port/")
        try {
            $listener.Start()
        } catch {
            $listener = [System.Net.HttpListener]::new()
            $listener.Prefixes.Add("http://localhost:$port/")
            $listener.Prefixes.Add("http://127.0.0.1:$port/")
            $listener.Start()
        }

        Write-Host "Watch agent listening on port $port"
        Write-Host "Project: $projectPath | Instance: $($server.Instance) | Server port: $($server.Port)"

        try {
            while ($listener.IsListening) {
                $ctx = $listener.GetContext()
                $route = "$($ctx.Request.HttpMethod) $($ctx.Request.Url.AbsolutePath)"

                try {
                    switch ($route) {
                        "GET /health"         { [WatchAgent]::SendJson($ctx, @{ status = "ok"; project = $projectPath }) }
                        "GET /server/status"  { [WatchAgent]::SendJson($ctx, $server.GetStatus()) }
                        "POST /server/start"  { $b = [WatchAgent]::ReadBody($ctx); [WatchAgent]::SendJson($ctx, $server.Start([bool]$b.watch)) }
                        "POST /server/stop"   { [WatchAgent]::SendJson($ctx, $server.Stop()) }
                        "POST /server/restart" {
                            $b = [WatchAgent]::ReadBody($ctx)
                            $r = $server.Restart([bool]$b.watch, [bool]$b.noBuild)
                            [WatchAgent]::SendJson($ctx, $r, $(if ($r.error) { 500 } else { 200 }))
                        }
                        "GET /server/log" {
                            $n = 50; $q = $ctx.Request.QueryString
                            if ($q["lines"]) { $n = [int]$q["lines"] }
                            [WatchAgent]::SendJson($ctx, $server.GetLog($n))
                        }
                        default { [WatchAgent]::SendJson($ctx, @{ error = "Not found" }, 404) }
                    }
                } catch {
                    Write-Host "Error: $_"
                    try { [WatchAgent]::SendJson($ctx, @{ error = $_.ToString() }, 500) } catch {}
                }
            }
        } finally {
            $listener.Stop()
            Write-Host "Watch agent stopped"
        }
    }

    # --- HTTP helpers ---

    static [void] SendJson([System.Net.HttpListenerContext]$ctx, [hashtable]$data) {
        [WatchAgent]::SendJson($ctx, $data, 200)
    }

    static [void] SendJson([System.Net.HttpListenerContext]$ctx, [hashtable]$data, [int]$code) {
        $json = $data | ConvertTo-Json -Depth 5 -Compress
        $buf = [System.Text.Encoding]::UTF8.GetBytes($json)
        $ctx.Response.StatusCode = $code
        $ctx.Response.ContentType = "application/json"
        $ctx.Response.ContentLength64 = $buf.Length
        $ctx.Response.OutputStream.Write($buf, 0, $buf.Length)
        $ctx.Response.OutputStream.Close()
    }

    static [hashtable] ReadBody([System.Net.HttpListenerContext]$ctx) {
        if ($ctx.Request.HasEntityBody) {
            $r = [System.IO.StreamReader]::new($ctx.Request.InputStream)
            $b = $r.ReadToEnd(); $r.Close()
            if ($b) { try { return $b | ConvertFrom-Json -AsHashtable } catch { return @{} } }
        }
        return @{}
    }
}
