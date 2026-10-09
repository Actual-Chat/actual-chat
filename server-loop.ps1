# server-loop.ps1
# Edit-run-restart loop for the Voxt server.
#
# Runs three steps in sequence:
#   1) npm-build     - npm run build:Debug
#   2) dotnet-build  - dotnet build -c $Configuration App.Server.csproj
#   3) server-run    - dotnet run   -c $Configuration App.Server.csproj -- -kb
#
# Steps 1 and 2 are skipped when nothing they consume changed since the last
# time they succeeded — see the tmp/server-loop-*.stamp files and
# Test-ChangedSince below.
#
# While Step 3 runs, creating tmp/server-loop-rebundle asks the loop to rebuild
# the bundle in place: npm only, no restart, the server process untouched.
#   pwsh:  New-Item -ItemType File tmp/server-loop-rebundle -Force
#   sh:    touch tmp/server-loop-rebundle
# Pressing 'j' in the loop terminal requests exactly the same thing, for when
# the developer already has the loop terminal focused.
#
# Creating tmp/server-loop-hard-restart (or pressing 'h') asks for a hard
# restart: stop the server, purge the WASM build outputs, then rebuild both
# steps from scratch. For when the browser caches assemblies that no longer
# match the server's and the page reload-loops on a mismatch — a state nothing
# inside the browser can recover from.
#   pwsh:  New-Item -ItemType File tmp/server-loop-hard-restart -Force
#   sh:    touch tmp/server-loop-hard-restart
#
# Pressing 'k' force-kills the server process on the spot, skipping
# /health/stop and the deadline that follows it — for a server too wedged to
# stop itself. It only changes how the process dies: a 'k' after 'h' still
# gets the hard restart's purge, a 'k' on its own restarts the loop normally.
#
# Per-step output goes to tmp/server-loop-<step>.log. Stage transitions are
# appended to tmp/server-loop.log. On any failure, a final marker line
# "Last step failed, remove this file to restart the loop." is appended to
# tmp/server-loop.log, and the script waits for either a keypress (in the
# loop terminal) or removal of tmp/server-loop.log to restart. A clean
# server exit (stop-style termination) also restarts the loop.
#
# All loop log files (tmp/server-loop.log + tmp/server-loop-<step>.log + the
# DevLog) are wiped at the start of every iteration.
#
# Scaling: keys 0-5 set the number of extra hosts, in both modes. The main host
# is always on the base port; extra host N is on base port + N and writes its own
# tmp/server-loop-server-run-<name>.{out,err,log}. Scaling down stops random
# extras gracefully. Every restart stops all hosts, wipes their logs and starts
# the main host plus as many extras as were running.
#
# Regular mode: the main host is the single OneServer (logs as usual); extras are
# more OneServer copies (server01, server02, ...), default 0. A main host started
# with no extras isn't distributed, so the first scale-up restarts the loop's
# server with -distributed.
#
# Multihost mode (-multihost argument, or the 'm' key to toggle it): the main host
# is an API server (-role:2:OneApiServer, log name 'api'), extras are backends
# (-role:2:OneBackendServer: backend01, backend02, ...), default 1; 0 leaves the
# API alone. It is the layout -multihost-role:2:OneApiServer produces, except
# that the loop owns every process.

[CmdletBinding()]
param(
    [Alias('c')]
    [string]$Configuration = "Debug",
    # Anything after the named params is forwarded to `dotnet run` AFTER
    # the `--` separator — i.e. it goes to App.Server.exe as command-line
    # arguments. Example:
    #   pwsh -NoProfile -File server-loop.ps1 -c Debug --foo --bar=baz
    # → `dotnet run -c Debug ... -- --foo --bar=baz`
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ServerArgs = @()
)

$ErrorActionPreference = "Continue"
$multiHost = $ServerArgs -contains '-multihost'
$ServerArgs = @($ServerArgs | Where-Object { $_ -ne '-multihost' })
$maxExtraHosts = 5
$ScriptDir = $PSScriptRoot
Set-Location $ScriptDir

$tmpDir = Join-Path $ScriptDir "tmp"
if (-not (Test-Path $tmpDir)) {
    New-Item -ItemType Directory -Path $tmpDir | Out-Null
}

$loopLog         = Join-Path $tmpDir "server-loop.log"
$npmBuildLog     = Join-Path $tmpDir "server-loop-npm-build.log"
$dotnetBuildLog  = Join-Path $tmpDir "server-loop-dotnet-build.log"
# Server-run produces three sibling files — one per stream, distinguished by
# extension. The DevLog (.log) is the richest source: it carries the full
# ActualChat structured diagnostics across all categories, not just whatever
# made it to stdout.
$serverRunBase   = Join-Path $tmpDir "server-loop-server-run"
$serverRunOutLog = "$serverRunBase.out"  # dotnet run stdout
$serverRunErrLog = "$serverRunBase.err"  # dotnet run stderr
$devLog          = "$serverRunBase.log"  # ActualChat_DevLog
$regularDevLog   = $devLog
$projectCsproj   = "src/dotnet/App.Server/App.Server.csproj"

$allLoopLogs = @($loopLog, $npmBuildLog, $dotnetBuildLog, $serverRunOutLog, $serverRunErrLog, $devLog)

# Sentinel any other process can create to ask the running loop for a fresh
# bundle without restarting the server. A file *and* the 'j' key: neither Claude
# nor the developer owns the loop terminal, and tmp/ is the one channel every
# environment (host, WSL, Docker) already shares — but when the developer does
# have the terminal focused, a keypress beats spelling out a path.
$rebundleFlag = Join-Path $tmpDir "server-loop-rebundle"

# Sentinel (or 'h') asking for a stop plus a purge of the WASM build outputs
# before the next iteration. The case it exists for: the browser holds cached
# assemblies that no longer match the ones the server serves, the page reload-
# loops on an assembly mismatch, and nothing reachable from inside the browser
# can recover it — an agent driving that tab is simply stuck. Dropping the
# stamps alone is not enough, because MSBuild considers the stale outputs
# up to date; they have to go.
$hardRestartFlag = Join-Path $tmpDir "server-loop-hard-restart"

# Purged by a hard restart. Scoped to the WASM app on purpose: it is the only
# output whose staleness the browser can be poisoned by, and wiping all of
# artifacts/ would turn a 1-minute recovery into a very long one.
$hardRestartPurgePaths = @(
    (Join-Path $ScriptDir "artifacts/obj/App.Wasm"),
    (Join-Path $ScriptDir "artifacts/bin/App.Wasm")
)

# How long a stop request gets before the loop force-kills the server itself.
# Deliberately shorter than the 15s in-process HardExit watchdog: that one only
# fires while the process is healthy enough to run it, and when it isn't, the
# wait buys nothing. Press 'k' to skip even this.
$stopDeadlineSeconds = 10

# Passed to every .NET build the loop runs, in every configuration.
#
# Build-time static-asset compression writes .gz copies under artifacts/obj that
# only `dotnet build` can refresh, and MapStaticAssets hands those to anything
# sending Accept-Encoding: gzip — so a rebundle (npm only) would leave the
# browser on the OLD bundle. Directory.Build.props disables compression
# globally, but App.Wasm.csproj re-enables it for Release; a global property
# overrides both, so -c Release stays as safe here as the default -c Debug.
# It's scoped to *build* deliberately: `dotnet publish` still precompresses,
# which is what real deployments want.
#
# Both Step 2 and Step 3 must pass it. Global properties are part of a project's
# build identity, so `dotnet build` with it and `dotnet run` without it are two
# different builds — MSBuild would redo the whole thing in Step 3.
$buildProperties = @('-p:DisableBuildCompression=true')

# Build stamps deliberately survive Reset-LoopLogs: each holds the UTC ticks
# captured just before its step last succeeded, and drives the "nothing
# changed, skip it" checks below. The dotnet one is per-configuration —
# switching -c leaves a different output tree, which the previous
# configuration's stamp says nothing about. npm always builds :Debug.
$npmStamp    = Join-Path $tmpDir "server-loop-npm-build.stamp"
$dotnetStamp = Join-Path $tmpDir "server-loop-dotnet-build.$($Configuration.ToLowerInvariant()).stamp"

$distBundleJs = Join-Path $ScriptDir "src/dotnet/App.Wasm/wwwroot/dist/bundle.js"

# Build outputs and package trees are never inputs; walking them would make
# every check report "changed", since MSBuild rewrites obj/ on every run.
$scanExcludedDirs = @('obj', 'bin', 'node_modules', '.git')

# Extensions that only ever reach the .NET compiler. Everything else under the
# npm roots counts as a bundle input — .razor and .cshtml included, because
# tailwind.config.js scans exactly those two for class names.
$dotnetOnlyExtensions = @(
    '.cs', '.csproj', '.sln', '.slnf', '.props', '.targets', '.resx',
    '.editorconfig', '.xaml', '.plist', '.storyboard', '.xib', '.swift'
)

# What build.mjs + tsc + tailwind + postcss actually consume: the TS/CSS trees,
# the .razor/.cshtml Tailwind scans, the assets copyAssets() copies, and the
# root-level config that shapes all of it.
$npmInputRoots = @(
    'src/nodejs', 'src/dotnet', 'resources/sounds/converted',
    'build.mjs', 'tsconfig.json', 'tailwind.config.js', 'postcss.config.mjs',
    'postcss-watch-plugin.js', 'package.json', 'package-lock.json',
    'firebase.config.json'
) | ForEach-Object { Join-Path $ScriptDir $_ }

# Everything App.Server.csproj can transitively compile or embed. Wider than
# the project graph on purpose — a superfluous build costs seconds, a skipped
# one costs a debugging session. Note this covers
# src/dotnet/App.Wasm/wwwroot/dist too, so a fresh bundle always counts as a
# .NET input: MapStaticAssets fingerprints the bundle at build time.
$dotnetInputRoots = @(
    'src', 'lib',
    'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props',
    'global.json', 'nuget.config', 'version.json'
) | ForEach-Object { Join-Path $ScriptDir $_ }

function Write-LoopLog([string]$Text) {
    $ts = Get-Date -Format "HH:mm:ss"
    $line = "[$ts] $Text"
    Write-Host $line
    Add-Content -Path $loopLog -Value $line -Encoding UTF8
}

function Reset-LoopLogs {
    foreach ($f in $allLoopLogs) {
        Remove-Item $f -Force -ErrorAction SilentlyContinue
    }
    # Per-host logs of multihost mode: server-loop-server-run-<name>.{out,err,log}
    Get-ChildItem -Path $tmpDir -Filter 'server-loop-server-run-*' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

# npm-build, shared by Step 1 and the in-place rebundle. Returns the exit code.
#
# On Windows `npm` is a `.cmd` wrapper. PowerShell's `&` + `*>` does not
# reliably propagate exit codes or capture all output from .cmd files, so tsc
# errors silently dropped the loop into a "passed" state. Run via `cmd /c` and
# redirect inside cmd to capture both streams faithfully.
function Invoke-NpmBuild {
    if ($IsWindows) {
        & cmd /c "npm run build:Debug > `"$npmBuildLog`" 2>&1"
    } else {
        & npm run build:Debug *> $npmBuildLog
    }
    return $LASTEXITCODE
}

# Stamp files hold "when did this step last succeed", as UTC ticks. The value
# is sampled *before* the step runs, so an edit made while it builds is still
# seen as newer and forces another pass.
function Get-StampTimeUtc([string]$Path) {
    try {
        if (-not (Test-Path -LiteralPath $Path)) { return $null }
        $ticks = [long]((Get-Content -LiteralPath $Path -Raw -ErrorAction Stop).Trim())
        return [datetime]::new($ticks, [System.DateTimeKind]::Utc)
    } catch {
        # Truncated / hand-edited stamp: treat as "never ran".
        return $null
    }
}

function Set-StampTimeUtc([string]$Path, [datetime]$TimeUtc) {
    Set-Content -LiteralPath $Path -Value ([string]$TimeUtc.Ticks) -Encoding UTF8
}

# Walks $Roots (files or directories) and answers "did anything change after
# $SinceUtc?". Directory timestamps are checked too, which is how deletions and
# renames register — no surviving file's mtime moves for those.
#
# Chosen over `git status` because the question is "newer than the last build",
# not "different from HEAD": a file edited and reverted, or edited twice, must
# still count, and untracked/ignored inputs (dist/, node_modules artifacts)
# matter as much as tracked ones.
#
# Failure modes, all deliberately biased toward running the build:
#   - unreadable stamp, enumeration error, clock moved backwards => "changed";
#   - a tool that preserves mtimes when writing (rsync -t, archive extraction
#     with timestamps, a restored backup) hides its change. That is the one way
#     this can under-build; deleting tmp/server-loop-*.stamp forces a full pass.
#
# Timestamps are read per entry rather than taken from the enumeration data:
# NTFS keeps a copy of them in the parent directory entry and refreshes it
# lazily, so an enumerated directory can still report the mtime it had before
# a file inside it was deleted. Measured at ~110ms for src/ (5.7k files, 690
# folders) — not worth trading for a wrong answer.
function Test-ChangedSince {
    param(
        [Parameter(Mandatory)] [datetime] $SinceUtc,
        [Parameter(Mandatory)] [string[]] $Roots,
        [string[]] $ExcludeDirNames = @(),
        [string[]] $IgnoreExtensions = @()
    )
    try {
        $pending = [System.Collections.Generic.Stack[System.IO.DirectoryInfo]]::new()
        foreach ($root in $Roots) {
            if ([System.IO.Directory]::Exists($root)) {
                $pending.Push([System.IO.DirectoryInfo]::new($root))
            } elseif ([System.IO.File]::Exists($root)) {
                if ([System.IO.File]::GetLastWriteTimeUtc($root) -gt $SinceUtc) { return $true }
            }
        }
        while ($pending.Count -gt 0) {
            $dir = $pending.Pop()
            if ([System.IO.Directory]::GetLastWriteTimeUtc($dir.FullName) -gt $SinceUtc) { return $true }
            foreach ($entry in $dir.EnumerateFileSystemInfos()) {
                if ($entry.Attributes.HasFlag([System.IO.FileAttributes]::Directory)) {
                    if ($ExcludeDirNames -notcontains $entry.Name) { $pending.Push($entry) }
                    continue
                }
                if ($IgnoreExtensions -contains $entry.Extension) { continue }
                if ([System.IO.File]::GetLastWriteTimeUtc($entry.FullName) -gt $SinceUtc) { return $true }
            }
        }
        return $false
    } catch {
        Write-LoopLog "Change scan failed ($_); assuming everything changed."
        return $true
    }
}

function Wait-ForRestart {
    Write-LoopLog "Last step failed, remove this file to restart the loop."
    Write-Host "Press any key in this terminal OR delete '$loopLog' to restart..." -ForegroundColor Yellow
    while (Test-Path $loopLog) {
        try {
            if ([System.Console]::KeyAvailable) {
                [void][System.Console]::ReadKey($true)
                break
            }
        } catch {
            # No interactive console (e.g. running detached); only file removal will break the wait.
        }
        Start-Sleep -Milliseconds 333
    }
}

# Returns the base URL for the running server. Reads .env when present so
# worktrees with a custom HostSettings__BaseUri (e.g. https://wt1.local.voxt.ai)
# stop the right instance.
function Get-BaseUri {
    $baseUri = "https://local.voxt.ai"
    $envFile = Join-Path $ScriptDir ".env"
    if (Test-Path $envFile) {
        Get-Content $envFile | ForEach-Object {
            if ($_ -match '^HostSettings__BaseUri=(.+)$') {
                $baseUri = $Matches[1].Trim().TrimEnd('/')
            }
        }
    }
    return $baseUri
}

# Sends a clean stop request to the running server. The 's'/'x' keys in the
# child's keyboard watcher don't reach it because we redirect stdout/stderr to
# capture logs (which detaches the inherited console on Windows), so we forward
# keypresses from the loop terminal via this HTTP path instead.
#
# 502 / 503 / connection-refused responses mean the upstream is already
# down (server has stopped or is mid-restart). That's the desired
# end-state, so we treat them as success and keep the log line quiet.
# Watchdog helpers: external liveness check used during Step 3 in case the
# in-process /health/stop hard-exit watchdog can never arm because the
# listener itself is unresponsive (NGINX 502, direct port refused).
#
# The .NET process publishes its bound port via a single log line:
#   "[ActualChat.Mesh.MeshWatcher] [+] Endpoint lock acquired: localhost:<port>"
# The line is stable across one dotnet run; the .log file is wiped at the
# start of every loop iteration, so the next process writes a fresh line.
function Get-ServerPortFromDevLog {
    if (-not (Test-Path $devLog)) { return $null }
    try {
        $line = Select-String -Path $devLog `
            -Pattern '\[ActualChat\.Mesh\.MeshWatcher\] \[\+\] Endpoint lock acquired: localhost:(\d+)' `
            -ErrorAction SilentlyContinue |
            Select-Object -Last 1
        if ($line -and $line.Matches.Count -gt 0) {
            return [int]$line.Matches[0].Groups[1].Value
        }
    } catch {
        # Log is currently being written; transient I/O errors are fine.
    }
    return $null
}

function Test-ServerProbe([int]$Port) {
    $url = "http://localhost:$Port/healthz/live"
    try {
        $resp = Invoke-WebRequest -Uri $url -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
        return ([int]$resp.StatusCode -ge 200) -and ([int]$resp.StatusCode -lt 400)
    } catch {
        return $false
    }
}

# Returns $null when the running server would hand out the bundle npm just
# wrote, or a sentence explaining why it wouldn't.
#
# An in-place rebundle changes files the server already made up its mind about.
# MapStaticAssets builds its endpoint table from the manifest dotnet-build
# emitted: the /dist/bundle.<fingerprint>.js URL, Content-Length and ETag are
# all frozen there. $buildProperties removes the second, worse trap (a
# precompressed .gz npm never touches), so the gzip probe below is now a check
# that the flag actually took effect. Both are cheap to observe and expensive to
# discover an hour later, so ask the server instead of guessing.
function Get-BundleServingVerdict([int]$Port) {
    # 127.0.0.1, not localhost: the latter resolves to ::1 first here and costs
    # ~2s per request falling back to IPv4, inside a loop that ticks at 200ms.
    $url = "http://127.0.0.1:$Port/dist/bundle.js"
    $client = $null
    try {
        $diskLength = (Get-Item -LiteralPath $distBundleJs -ErrorAction Stop).Length
        $handler = [System.Net.Http.HttpClientHandler]::new()
        # Transparent decompression strips the very Content-Encoding /
        # Content-Length headers this function reads.
        $handler.AutomaticDecompression = [System.Net.DecompressionMethods]::None
        $handler.UseProxy = $false
        $client = [System.Net.Http.HttpClient]::new($handler)
        $client.Timeout = [TimeSpan]::FromSeconds(10)
        # GetAwaiter().GetResult() rather than .Result throughout: a property
        # getter that throws yields $null in PowerShell, which turns a
        # connection failure into a confusing null-reference message three
        # lines later.
        $gzipRequest = [System.Net.Http.HttpRequestMessage]::new('HEAD', $url)
        $gzipRequest.Headers.TryAddWithoutValidation('Accept-Encoding', 'gzip') | Out-Null
        $gzipResponse = $client.SendAsync($gzipRequest).GetAwaiter().GetResult()
        $servesPrecompressed = (($gzipResponse.Content.Headers.ContentEncoding -join ',') -match 'gzip')
        $gzipResponse.Dispose()
        if ($servesPrecompressed) {
            return "the server still serves a precompressed /dist copy, so $($buildProperties -join ' ') didn't take — that .gz is dotnet-build output npm cannot refresh, and anything sending Accept-Encoding: gzip keeps getting the OLD bundle. Most likely this server was launched by an older server-loop.ps1: restart the loop. Until then, stop the server for a full rebuild instead of rebundling."
        }

        # Sending no Accept-Encoding selects the identity variant — the only one
        # mapped straight onto the file npm just wrote. ResponseHeadersRead +
        # Dispose keeps the 4 MB body off the wire.
        $request = [System.Net.Http.HttpRequestMessage]::new('GET', $url)
        $response = $client.SendAsync($request, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        $servedLength = $response.Content.Headers.ContentLength
        $response.Dispose()
        if ($servedLength -ne $diskLength) {
            return "the server still advertises $servedLength bytes for /dist/bundle.js while npm just wrote $diskLength — its static-asset manifest predates this bundle. Stop the server for a full rebuild."
        }
        return $null
    } catch {
        return "couldn't verify what the server serves ($($_.Exception.GetBaseException().Message))."
    } finally {
        if ($client) { $client.Dispose() }
    }
}

# Services one rebundle request, whatever asked for it — the sentinel file or
# the 'j' key. Runs on the loop's own thread and never touches $proc.
#
# The watchdog counters are [ref]s rather than return values because npm holds
# this single-threaded loop for the whole build: they have to be re-armed even
# when it fails, and the caller shouldn't be able to forget to do it.
function Invoke-Rebundle {
    param(
        # The keyboard-stop deadline, or $null when no stop is in flight.
        $StopDeadline,
        [Parameter(Mandatory)] [ref] $WatchdogPort,
        [Parameter(Mandatory)] [ref] $WatchdogMissCount,
        [Parameter(Mandatory)] [ref] $WatchdogNextProbeAt
    )
    if ($StopDeadline) {
        Write-LoopLog "Rebundle: ignored — a server stop is already in flight."
        return
    }

    Write-LoopLog "Rebundle: npm run build:Debug (server keeps running)..."
    $startedAt = [datetime]::UtcNow
    $exitCode = Invoke-NpmBuild
    $seconds = ([datetime]::UtcNow - $startedAt).TotalSeconds
    if ($exitCode -ne 0) {
        Write-LoopLog ("Rebundle: FAILED (exit code {0}) after {1:N1}s — see {2}. Server left running; fix and request another one." -f `
            $exitCode, $seconds, $npmBuildLog)
    } else {
        Set-StampTimeUtc $npmStamp $startedAt
        Write-LoopLog ("Rebundle: done in {0:N1}s." -f $seconds)
        if (-not $WatchdogPort.Value) { $WatchdogPort.Value = Get-ServerPortFromDevLog }
        $verdict = if ($WatchdogPort.Value) { Get-BundleServingVerdict $WatchdogPort.Value } else { $null }
        if ($verdict) {
            Write-LoopLog "Rebundle: WARNING — $verdict"
        } else {
            Write-LoopLog "Rebundle: the server serves the new bundle — reload the page with caching disabled (the /dist/bundle.<hash>.js URL is unchanged and cached as immutable)."
        }
    }
    # npm just held this single-threaded loop for seconds to minutes, so every
    # watchdog counter is stale evidence: no probe could run while it did, and
    # node may still be starving the box for a moment after it exits. Zeroing
    # the miss count and re-arming a few seconds out is what keeps a slow first
    # post-build probe from combining with a pre-build miss into the 2-miss
    # force-kill. Erring here only ever delays a kill by one interval; erring
    # the other way kills the developer's server mid-build.
    $WatchdogMissCount.Value = 0
    $WatchdogNextProbeAt.Value = (Get-Date).AddSeconds(5)
}

function Start-ServerProcess {
    param(
        [Parameter(Mandatory)] [string]   $Configuration,
        [Parameter(Mandatory)] [string]   $ProjectCsproj,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $BuildProperties,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ServerArgs,
        [Parameter(Mandatory)] [string]   $StdoutPath,
        [Parameter(Mandatory)] [string]   $StderrPath,
        [switch] $NoBuild
    )
    $noBuildArgs = if ($NoBuild) { @('--no-build') } else { @() }
    if ($IsWindows) {
        $runArgs = @('run', '-c', $Configuration, '--no-launch-profile', '--project', $ProjectCsproj) + $noBuildArgs + $BuildProperties + @('--') + $ServerArgs
        return Start-Process -FilePath dotnet `
            -ArgumentList $runArgs `
            -RedirectStandardOutput $StdoutPath -RedirectStandardError $StderrPath `
            -NoNewWindow -PassThru
    }
    # POSIX: Start-Process with -RedirectStandardOutput/-RedirectStandardError
    # attaches an AsyncStreamReader to the child's stdio. On force-kill
    # (Stop-Process -Force / watchdog kill) the StreamWriter backing the
    # redirect file is disposed while the reader still has buffered lines;
    # the next FlushMessageQueue throws ObjectDisposedException on a
    # threadpool thread, escapes $ErrorActionPreference, and aborts the
    # host (the "Cannot write to a closed TextWriter" / "Abort trap: 6"
    # symptom). Workaround: launch via System.Diagnostics.Process directly
    # with no PowerShell-owned readers, and have /bin/sh do the redirection
    # then `exec` dotnet so we still get the dotnet PID. (Start-Process
    # -NoNewWindow without -Redirect* on macOS turned out to detach the
    # child early; the direct API does not.)
    $quotedArgs = ($ServerArgs | ForEach-Object { "'$(($_ -replace "'", "'\''"))'" }) -join ' '
    $quotedProps = ($BuildProperties | ForEach-Object { "'$(($_ -replace "'", "'\''"))'" }) -join ' '
    $quotedNoBuild = $noBuildArgs -join ' '
    $shellLine = "exec dotnet run -c '$Configuration' --no-launch-profile --project '$ProjectCsproj' $quotedNoBuild $quotedProps -- $quotedArgs > '$StdoutPath' 2> '$StderrPath'"
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = '/bin/sh'
    $psi.ArgumentList.Add('-c')
    $psi.ArgumentList.Add($shellLine)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $false
    $psi.RedirectStandardOutput = $false
    $psi.RedirectStandardError = $false
    return [System.Diagnostics.Process]::Start($psi)
}

# The loop's single force-kill path: the stop deadline, the watchdog's second
# miss and the 'k' key all end up here, and each labels its own log lines.
function Stop-ServerProcessNow {
    param(
        [Parameter(Mandatory)] $Process,
        [Parameter(Mandatory)] [string] $Label,
        [Parameter(Mandatory)] [string] $Reason
    )
    Write-LoopLog "${Label}: $Reason force-killing PID $($Process.Id)."
    try { Stop-Process -Id $Process.Id -Force -ErrorAction Stop }
    catch { Write-LoopLog "${Label}: Stop-Process failed: $_" }
}

function Send-StopSignal {
    $url = "$(Get-BaseUri)/health/stop"
    Write-LoopLog "Forwarding stop request to $url..."
    try {
        Invoke-WebRequest -Uri $url -Method Get -TimeoutSec 5 -SkipCertificateCheck -UseBasicParsing | Out-Null
    } catch {
        $resp = $_.Exception.Response
        $status = $null
        if ($resp -is [System.Net.Http.HttpResponseMessage]) { $status = [int]$resp.StatusCode }
        elseif ($resp -is [System.Net.HttpWebResponse]) { $status = [int]$resp.StatusCode }
        # 502/503 from nginx + raw socket errors all mean "upstream gone" —
        # which is what stop is trying to achieve. Don't yell about it.
        $upstreamGone = ($status -in 502, 503, 504) -or ($_.Exception -is [System.Net.WebException] -and $resp -eq $null)
        if ($upstreamGone) {
            Write-LoopLog "Stop request: upstream already down (treating as success)."
        } else {
            Write-LoopLog "Stop request failed: $_"
        }
    }
}

# Hosts are hashtables: Name, Role, Port, Process, OutLog, ErrLog, DevLog,
# StopDeadline. The loop's main wait (stop deadline, watchdog, exit handling)
# follows the main host (the API in multihost mode, the single server in
# regular mode); extras are pruned from the list as they exit.
$hosts = [System.Collections.Generic.List[hashtable]]::new()
$mainHost = $null
# Kept while the loop runs, across server restarts; entering multihost mode starts with one backend
$extraCount = if ($multiHost) { 1 } else { 0 }
$mainDistributed = $false
$extraPrefix = 'backend'
$extraRole = 'OneBackendServer'
$extraRoleGroup = '2'
$extraLabel = 'backends'
$meshLockSubspace = $null
$basePort = 7080
$runMultiHost = $false

# HostSettings__BasePort in .env is what ai.ps1 sets for worktrees; the Aspire
# host reads it the same way.
function Get-BasePort {
    $port = 7080
    $envFile = Join-Path $ScriptDir ".env"
    if (Test-Path $envFile) {
        Get-Content $envFile | ForEach-Object {
            if ($_ -match '^HostSettings__BasePort=(\d+)\s*$') { $port = [int]$Matches[1] }
        }
    }
    return $port
}

# --no-build: Step 2 has just built (or found up to date) the one project every
# host runs, and several `dotnet run` builds racing over the same obj/ is
# exactly what we don't want.
function Start-MultiHost {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Role,
        [Parameter(Mandatory)] [int]    $Port,
        [Parameter(Mandatory)] [string] $RoleGroup
    )
    $base = Join-Path $tmpDir "server-loop-server-run-$Name"
    $h = @{
        Name = $Name; Role = $Role; Port = $Port
        OutLog = "$base.out"; ErrLog = "$base.err"; DevLog = "$base.log"
        StopDeadline = $null
    }
    $env:ActualChat_DevLog = $h.DevLog
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:HostSettings__MeshLockSubspace = $meshLockSubspace
    $env:HostSettings__MeshLockOptionsPreset = 'Default'
    try {
        $h.Process = Start-ServerProcess `
            -Configuration    $Configuration `
            -ProjectCsproj    $projectCsproj `
            -BuildProperties  $buildProperties `
            -ServerArgs       (@("-role:$($RoleGroup):$Role", "-url:http://localhost:$Port", '-distributed') + $ServerArgs) `
            -StdoutPath       $h.OutLog `
            -StderrPath       $h.ErrLog `
            -NoBuild
    } finally {
        Remove-Item Env:HostSettings__MeshLockSubspace, Env:HostSettings__MeshLockOptionsPreset -ErrorAction SilentlyContinue
    }
    Write-LoopLog "Started $Name ($Role) on port $Port, PID $($h.Process.Id), log $($h.DevLog)."
    return $h
}

# Extras are named <prefix><XX> (backend01, server01, ...), XX = two-digit hex
# id: the first one with no running host and no log left from this iteration
# (logs are wiped at its start, so a stopped extra's id isn't reused until the
# next restart). The port is base + id.
function New-ExtraId {
    for ($id = 1; $id -le 255; $id++) {
        $name = '{0}{1:x2}' -f $extraPrefix, $id
        if ($hosts | Where-Object { $_.Name -eq $name }) { continue }
        if (Test-Path (Join-Path $tmpDir "server-loop-server-run-$name.log")) { continue }
        return $id
    }
    return $null
}

function Start-Extra([int]$Id) {
    $hosts.Add((Start-MultiHost ('{0}{1:x2}' -f $extraPrefix, $Id) $extraRole ($basePort + $Id) $extraRoleGroup))
}

function Get-ActiveExtras {
    return @($hosts | Where-Object { $_ -ne $mainHost -and -not $_.Process.HasExited -and -not $_.StopDeadline })
}

# Starts or gracefully stops random extras until $Target of them are running.
function Set-ExtraCount([int]$Target) {
    $active = @(Get-ActiveExtras)
    while ($active.Count -lt $Target) {
        $id = New-ExtraId
        if ($null -eq $id) {
            Write-LoopLog "No free host id left."
            break
        }
        Start-Extra $id
        $active = @(Get-ActiveExtras)
    }
    if ($active.Count -gt $Target) {
        foreach ($victim in ($active | Get-Random -Count ($active.Count - $Target))) {
            Write-LoopLog "Stopping $($victim.Name) (PID $($victim.Process.Id))."
            Send-HostStop $victim
        }
    }
    $script:extraCount = $Target
    Write-HostList
}

function Write-HostList {
    $extras = @($hosts | Where-Object { $_ -ne $mainHost -and -not $_.Process.HasExited })
    $items = @($extras | ForEach-Object {
        $state = if ($_.StopDeadline) { ' (stopping)' } else { '' }
        "$($_.Name) :$($_.Port) PID $($_.Process.Id)$state"
    })
    $list = if ($items.Count -gt 0) { $items -join '; ' } else { 'none' }
    Write-LoopLog "Hosts: $($mainHost.Name) :$($mainHost.Port); $extraLabel ($($extras.Count)): $list."
}

function Send-HostStop($H) {
    try {
        Invoke-WebRequest -Uri "http://127.0.0.1:$($H.Port)/health/stop" -TimeoutSec 5 -UseBasicParsing | Out-Null
    } catch {
        Write-LoopLog "Stop request to $($H.Name) failed: $($_.Exception.Message)"
    }
    if (-not $H.StopDeadline) { $H.StopDeadline = (Get-Date).AddSeconds($stopDeadlineSeconds) }
}

function Stop-HostNow($H, [string]$Reason) {
    Write-LoopLog "$($H.Name): $Reason force-killing PID $($H.Process.Id) and its children."
    try { $H.Process.Kill($true) }
    catch { Write-LoopLog "$($H.Name): kill failed: $_" }
}

# One stop request for whatever is running: the main host (through its own
# stop signal in regular mode) and every extra.
function Request-ServerStop([bool]$Multi) {
    if (-not $Multi) { Send-StopSignal }
    foreach ($h in $hosts) {
        if ($h -eq $mainHost -and -not $Multi) { continue }
        if (-not $h.Process.HasExited) { Send-HostStop $h }
    }
}

# Used once the API has exited: nothing may outlive an iteration, because the
# next one wipes the logs these processes still write to.
function Stop-AllHosts {
    foreach ($h in $hosts) {
        if (-not $h.Process.HasExited) { Send-HostStop $h }
    }
    while ($true) {
        $running = @($hosts | Where-Object { -not $_.Process.HasExited })
        if ($running.Count -eq 0) { break }
        foreach ($h in $running) {
            if ((Get-Date) -ge $h.StopDeadline) {
                Stop-HostNow $h "graceful shutdown didn't complete within ${stopDeadlineSeconds}s;"
                $h.StopDeadline = (Get-Date).AddSeconds($stopDeadlineSeconds)
            }
        }
        Start-Sleep -Milliseconds 200
    }
    $hosts.Clear()
}

# One-time banner: listing log paths here keeps per-iteration output terse.
Write-Host "server-loop log files (wiped at the start of every iteration):"
Write-Host "  loop          $loopLog"
Write-Host "  npm-build     $npmBuildLog"
Write-Host "  dotnet-build  $dotnetBuildLog"
Write-Host "  server-run    $serverRunOutLog (stdout)"
Write-Host "                $serverRunErrLog (stderr)"
Write-Host "                $devLog (DevLog)"
Write-Host "extra hosts (keys 0-5 scale them; -multihost argument or 'm' key: API + backends instead of one server), one log set per extra host:"
Write-Host "  $tmpDir/server-loop-server-run-<api|backendXX|serverXX>.{out,err,log}"
Write-Host "Press 'j' here, or create $rebundleFlag, while the server runs to rebuild the bundle without restarting it."
Write-Host "Press 'h' here, or create $hardRestartFlag, to stop, purge the WASM build outputs and rebuild from scratch."
Write-Host ""

$hardRestartRequested = $false

while ($true) {
    Reset-LoopLogs
    # A rebundle asked for while the loop was between servers is already
    # covered by Step 1 below — drop it instead of firing a redundant npm build
    # seconds after the fresh one.
    Remove-Item $rebundleFlag -Force -ErrorAction SilentlyContinue
    # Purge here rather than where it was asked for: the server still held the
    # files then. Dropping the stamps too, or Steps 1 and 2 would skip and the
    # purged outputs would never be rebuilt.
    if ($hardRestartRequested) {
        $hardRestartRequested = $false
        Remove-Item $npmStamp, $dotnetStamp -Force -ErrorAction SilentlyContinue
        foreach ($path in $hardRestartPurgePaths) {
            if (-not (Test-Path $path)) { continue }

            try {
                Remove-Item $path -Recurse -Force -ErrorAction Stop
                Write-LoopLog "Hard restart: purged $path."
            }
            catch {
                Write-LoopLog "Hard restart: could not purge $path - $_"
            }
        }
        Write-LoopLog "Hard restart: stamps dropped, so npm-build and dotnet-build both run below."
    }
    $failureMessage = $null

    # Step 1: npm-build
    $npmStartedAt = [datetime]::UtcNow
    $npmStampAt = Get-StampTimeUtc $npmStamp
    $npmChanged = (-not $npmStampAt) -or (Test-ChangedSince `
        -SinceUtc         $npmStampAt `
        -Roots            $npmInputRoots `
        -ExcludeDirNames  ($scanExcludedDirs + 'dist') `
        -IgnoreExtensions $dotnetOnlyExtensions)
    if (-not $npmChanged) {
        Write-LoopLog "Step 1/3 (npm-build) — skipped, no bundle input changed since the last one."
    } else {
        Write-LoopLog "Step 1/3 (npm-build)"
        $npmExitCode = Invoke-NpmBuild
        if ($npmExitCode -ne 0) {
            $failureMessage = "Step 1 (npm-build) failed with exit code $npmExitCode."
        } else {
            Set-StampTimeUtc $npmStamp $npmStartedAt
        }
    }

    # Step 2: dotnet-build
    #
    # Skipping this never moves work off the clock, it only avoids doing it
    # twice: Step 3 runs `dotnet run`, which builds on its own. So the only
    # skip worth making is the one where the build would find nothing to do —
    # nothing under $dotnetInputRoots changed, which after Step 1 also
    # means the bundle on disk is the one the last manifest was fingerprinted
    # from. Skipping on "only bundle files changed" would be wrong for the
    # opposite reason: dist/ *is* a .NET input, and `dotnet run` would just
    # rebuild it in Step 3 with worse logs.
    if (-not $failureMessage) {
        $dotnetStartedAt = [datetime]::UtcNow
        $dotnetStampAt = Get-StampTimeUtc $dotnetStamp
        $dotnetChanged = (-not $dotnetStampAt) -or (Test-ChangedSince `
            -SinceUtc        $dotnetStampAt `
            -Roots           $dotnetInputRoots `
            -ExcludeDirNames $scanExcludedDirs)
        if (-not $dotnetChanged) {
            Write-LoopLog "Step 2/3 (dotnet-build) — skipped, no .NET input changed since the last one."
        } else {
            Write-LoopLog "Step 2/3 (dotnet-build)"
            & dotnet build -c $Configuration @buildProperties $projectCsproj *> $dotnetBuildLog
            if ($LASTEXITCODE -ne 0) {
                $failureMessage = "Step 2 (dotnet-build) failed with exit code $LASTEXITCODE."
            } else {
                Set-StampTimeUtc $dotnetStamp $dotnetStartedAt
            }
        }
    }

    # Step 3: server-run
    if (-not $failureMessage) {
        Write-LoopLog "Step 3/3 (server-run)"
        # Captured per iteration: the 'm' key flips $multiHost for the next one.
        $runMultiHost = $multiHost
        $hosts.Clear()
        # Keys pressed while the steps above were running are still buffered:
        # digits set the extra-host count for this start, other keys are dropped
        # (they'd stop the server the moment it is up).
        try {
            while ([System.Console]::KeyAvailable) {
                $buffered = [System.Console]::ReadKey($true).KeyChar
                if ($buffered -match '^[0-5]$') {
                    $extraCount = [int]::Parse([string]$buffered)
                    Write-LoopLog "Key '$buffered' pressed during the build: starting with $extraCount extra host(s)."
                }
            }
        } catch { }
        $extraPrefix = if ($runMultiHost) { 'backend' } else { 'server' }
        $extraRole = if ($runMultiHost) { 'OneBackendServer' } else { 'OneServer' }
        $extraRoleGroup = if ($runMultiHost) { '2' } else { '1' }
        $extraLabel = if ($runMultiHost) { 'backends' } else { 'servers' }
        $basePort = Get-BasePort
        # Fresh subspace per iteration, so mesh locks of hosts that were
        # force-killed can't hold up the next set.
        $meshLockSubspace = -join (((48..57) + (97..122)) | Get-Random -Count 8 | ForEach-Object { [char]$_ })
        if ($runMultiHost) {
            $devLog = Join-Path $tmpDir "server-loop-server-run-api.log"
            Write-LoopLog "Multihost mode: API on port $basePort, mesh lock subspace '$meshLockSubspace'."
            $mainHost = Start-MultiHost 'api' 'OneApiServer' $basePort '2'
            $hosts.Add($mainHost)
            $proc = $mainHost.Process
            Set-ExtraCount $extraCount
            Write-Host "Keyboard: 0-5 = number of backends to run (now $extraCount); 'm' = switch to regular mode (restart); 'j' = rebundle in place; 'h' = hard restart; 'k' = kill all hosts right now; any other key = stop all hosts (force-kill after ${stopDeadlineSeconds}s)." -ForegroundColor Cyan
        } else {
            $devLog = $regularDevLog
            $env:ActualChat_DevLog = $devLog
            $env:ASPNETCORE_ENVIRONMENT = "Development"
            # Run dotnet asynchronously so this PowerShell can poll its own stdin
            # while the server is up. Otherwise `*>` redirection makes 's'/'x' keys
            # invisible to the child's Console.ReadKey watcher (the child no longer
            # has a TTY for stdin on Windows). On 's'/'x' from the loop terminal we
            # forward a /health/stop instead - which triggers the same clean
            # IHostApplicationLifetime.StopApplication path the child would have.
            # Stdout and stderr go to separate sibling files so output streams stay
            # uninterleaved - the .err file is empty on a healthy run.
            # Args after `--` are forwarded to App.Server.exe. The loop owns
            # keypress handling itself ('j' rebundles, anything else goes to
            # /health/stop), so the App.Server-side `-kb` keyboard watcher
            # would have nothing to read - its stdin is captured by the
            # redirect anyway. We don't pass `-kb`. `$ServerArgs` appends
            # whatever the operator passed to server-loop.ps1.
            # Extras are more OneServer copies in the same mesh, so with any
            # planned the main one has to be distributed too.
            $mainDistributed = $extraCount -gt 0
            $mainArgs = @(if ($mainDistributed) { "-distributed" }) + @($ServerArgs)
            if ($mainDistributed) {
                $env:HostSettings__MeshLockSubspace = $meshLockSubspace
                $env:HostSettings__MeshLockOptionsPreset = 'Default'
            }
            try {
                $proc = Start-ServerProcess `
                    -Configuration    $Configuration `
                    -ProjectCsproj    $projectCsproj `
                    -BuildProperties  $buildProperties `
                    -ServerArgs       $mainArgs `
                    -StdoutPath       $serverRunOutLog `
                    -StderrPath       $serverRunErrLog
            } finally {
                Remove-Item Env:HostSettings__MeshLockSubspace, Env:HostSettings__MeshLockOptionsPreset -ErrorAction SilentlyContinue
            }
            $mainHost = @{
                Name = 'main'; Role = 'OneServer'; Port = $basePort; Process = $proc
                OutLog = $serverRunOutLog; ErrLog = $serverRunErrLog; DevLog = $devLog; StopDeadline = $null
            }
            $hosts.Add($mainHost)
            if ($mainDistributed) { Set-ExtraCount $extraCount }
            Write-Host "Keyboard: 0-5 = number of extra servers to run (now $extraCount); 'j' = rebundle in place (npm only, server keeps running); 'h' = hard restart (stop, purge the WASM build outputs, full rebuild); 'k' = kill all hosts right now; 'm' = switch to multihost mode (restart); any other key = stop the server (forwarded to /health/stop; force-kill after ${stopDeadlineSeconds}s)." -ForegroundColor Cyan
        }

        # Watchdog state: probe /healthz/live once we've learned the port
        # from the MeshWatcher log line. Two consecutive misses
        # -> Stop-Process -Force on $proc so the loop can recycle into a
        # fresh build. First probe lands 10s after process start (the
        # MeshWatcher line should have been written by then); subsequent
        # probes every 15s. If the port isn't in the log when a probe is
        # due, that itself counts as a missed probe.
        $watchdogPort = $null
        $watchdogMissCount = 0
        $watchdogAnnounced = $false
        $watchdogNextProbeAt = (Get-Date).AddSeconds(10)
        # Stop deadline: set whenever a stop goes out — a keypress other than
        # 'j'/'k', or a hard restart. If $proc hasn't exited by then we bypass
        # /health/stop entirely with Stop-Process -Force.
        $keyStopForceKillAt = $null
        # Set whenever the loop knows it caused the exit (stop signal sent
        # or watchdog killed the process). On exit we treat non-zero codes
        # as "stop-style termination" instead of a start failure, since
        # FailFast / Stop-Process -Force never exit 0.
        $stopRequested = $false

        while (-not $proc.HasExited) {
            $rebundleRequested = $false
            try {
                if ([System.Console]::KeyAvailable) {
                    # Capture the key instead of discarding it: 'j', 'h' and 'k'
                    # have to be claimed here, ahead of the catch-all stop below,
                    # or e.g. the developer's rebundle request would kill their
                    # server.
                    $key = [System.Console]::ReadKey($true)
                    $rawChar = $key.KeyChar
                    $keyChar = [char]::ToLowerInvariant($rawChar)
                    if ($rawChar -match '^[0-5]$') {
                        $target = [int]::Parse([string]$rawChar)
                        if ($stopRequested) {
                            $extraCount = $target
                            Write-LoopLog "'$rawChar': a stop is in flight, the next start will have $target extra host(s)."
                        } elseif (-not $runMultiHost -and -not $mainDistributed -and $target -gt 0) {
                            Write-LoopLog "Scaling to $target extra server(s): restarting, since the main server isn't distributed."
                            $extraCount = $target
                            Request-ServerStop $runMultiHost
                            $stopRequested = $true
                            $keyStopForceKillAt = (Get-Date).AddSeconds($stopDeadlineSeconds)
                        } else {
                            Set-ExtraCount $target
                        }
                    }
                    elseif ($keyChar -eq 'm') {
                        $multiHost = -not $multiHost
                        $nextMode = if ($multiHost) { 'multihost' } else { 'regular' }
                        $extraCount = if ($multiHost) { 1 } else { 0 }
                        if ($stopRequested) {
                            Write-LoopLog "Mode switch: the next iteration runs in $nextMode mode."
                        } else {
                            Write-LoopLog "Mode switch: restarting in $nextMode mode."
                            Request-ServerStop $runMultiHost
                            $stopRequested = $true
                            $keyStopForceKillAt = (Get-Date).AddSeconds($stopDeadlineSeconds)
                        }
                    }
                    elseif ($keyChar -eq 'j') {
                        $rebundleRequested = $true
                    }
                    elseif ($keyChar -eq 'h') {
                        $hardRestartRequested = $true
                    }
                    elseif ($keyChar -eq 'k') {
                        # Kill now, no /health/stop and no deadline: the endpoint
                        # is useless precisely when the server is wedged — a
                        # deadlocked startup, a hung disposer, a listener that
                        # never bound — and then the wait is pure delay.
                        # Claimed ahead of the stop guard below so it also cuts
                        # short a stop already in flight.
                        #
                        # $hardRestartRequested is deliberately left as it is:
                        # 'k' decides how the process dies, 'h' decides what the
                        # next iteration rebuilds. So 'k' alone recycles the loop
                        # normally, and 'h' then 'k' still purges below.
                        Stop-ServerProcessNow -Process $proc -Label 'Kill' -Reason 'requested from the loop terminal;'
                        foreach ($h in $hosts) {
                            if ($h -ne $mainHost -and -not $h.Process.HasExited) {
                                Stop-HostNow $h 'requested from the loop terminal;'
                            }
                        }
                        $stopRequested = $true
                        $keyStopForceKillAt = $null
                    }
                    elseif (-not $keyStopForceKillAt) {
                        Request-ServerStop $runMultiHost
                        $stopRequested = $true
                        $keyStopForceKillAt = (Get-Date).AddSeconds($stopDeadlineSeconds)
                        Write-LoopLog "Stop signal sent; will force-kill PID $($proc.Id) if it doesn't exit within ${stopDeadlineSeconds}s."
                    }
                    # Subsequent keypresses while we wait are no-ops; the
                    # deadline already covers the "stop didn't take" case, and
                    # 'k' is there for when it shouldn't be waited out.
                }
            } catch {
                # Detached / no console — fall back to passive wait.
            }

            # Bundle-only fast path, second trigger. Anything that can write to
            # tmp/ — Claude in Docker, the developer in another terminal, a
            # script — drops the sentinel. The flag is removed *before* the
            # build so a request landing while npm runs queues another pass
            # instead of being swallowed.
            if (Test-Path $rebundleFlag) {
                Remove-Item $rebundleFlag -Force -ErrorAction SilentlyContinue
                $rebundleRequested = $true
            }

            if (Test-Path $hardRestartFlag) {
                Remove-Item $hardRestartFlag -Force -ErrorAction SilentlyContinue
                $hardRestartRequested = $true
            }

            # A hard restart is a stop plus a purge, so it goes down the same
            # path as a keyboard stop - the purge itself waits for the process
            # to release the files.
            #
            # Guarded on $stopRequested rather than the deadline: the flag stays
            # set until the process exits, so anything that clears the deadline
            # first - the force-kill below, or 'k' - would otherwise make this
            # send a second stop and re-arm a deadline on a dying process.
            if ($hardRestartRequested -and -not $stopRequested) {
                Write-LoopLog "Hard restart requested: stopping the server, then purging the WASM build outputs."
                Request-ServerStop $runMultiHost
                $stopRequested = $true
                $keyStopForceKillAt = (Get-Date).AddSeconds($stopDeadlineSeconds)
            }

            if ($rebundleRequested) {
                # Runs on this thread, so nothing else in the loop ticks until
                # npm is done — see Invoke-Rebundle on why that matters.
                Invoke-Rebundle `
                    -StopDeadline         $keyStopForceKillAt `
                    -WatchdogPort         ([ref]$watchdogPort) `
                    -WatchdogMissCount    ([ref]$watchdogMissCount) `
                    -WatchdogNextProbeAt  ([ref]$watchdogNextProbeAt)
            }

            # Extras only: the main host's own deadline is $keyStopForceKillAt below.
            foreach ($h in @($hosts)) {
                if ($h -eq $mainHost) { continue }
                if ($h.Process.HasExited) {
                    Write-LoopLog "$($h.Name) exited (code $($h.Process.ExitCode))."
                    [void]$hosts.Remove($h)
                    Write-HostList
                } elseif ($h.StopDeadline -and (Get-Date) -ge $h.StopDeadline) {
                    Stop-HostNow $h "graceful shutdown didn't complete within ${stopDeadlineSeconds}s;"
                    $h.StopDeadline = (Get-Date).AddSeconds($stopDeadlineSeconds)
                }
            }

            if ($keyStopForceKillAt -and (Get-Date) -ge $keyStopForceKillAt) {
                Stop-ServerProcessNow -Process $proc -Label 'Keyboard-stop' `
                    -Reason "graceful shutdown didn't complete within ${stopDeadlineSeconds}s;"
                $stopRequested = $true
                # Clear the deadline so we don't retry on the next tick.
                # $proc.HasExited will flip after Stop-Process.
                $keyStopForceKillAt = $null
            }

            if ((Get-Date) -ge $watchdogNextProbeAt) {
                if (-not $watchdogPort) {
                    $watchdogPort = Get-ServerPortFromDevLog
                }
                $probeOk = $false
                if ($watchdogPort) {
                    if (-not $watchdogAnnounced) {
                        Write-LoopLog "Watchdog: started — probing http://localhost:$watchdogPort/healthz/live every 15s, kill after 2 misses."
                        $watchdogAnnounced = $true
                    }
                    $probeOk = Test-ServerProbe $watchdogPort
                }

                if ($probeOk) {
                    $watchdogMissCount = 0
                } else {
                    $watchdogMissCount++
                    if ($watchdogPort) {
                        Write-LoopLog "Watchdog: miss $watchdogMissCount/2 on port $watchdogPort (PID $($proc.Id))."
                    } else {
                        Write-LoopLog "Watchdog: miss $watchdogMissCount/2 — MeshWatcher port not yet in $devLog."
                    }
                    if ($watchdogMissCount -ge 2) {
                        Stop-ServerProcessNow -Process $proc -Label 'Watchdog' -Reason 'two consecutive misses;'
                        $stopRequested = $true
                        # $proc.HasExited will flip on the next tick.
                    }
                }
                $watchdogNextProbeAt = (Get-Date).AddSeconds(15)
            }

            Start-Sleep -Milliseconds 200
        }
        Stop-AllHosts
        $exitCode = $proc.ExitCode
        # Did the server reach "I bound a port" before exiting? If so any
        # exit is a post-start termination (stop signal in any of its
        # forms — keyboard, /health/stop, DebugUI, watchdog, in-process
        # HardExit) and we restart. If the MeshWatcher line never
        # appeared, the process never finished startup — that's a real
        # failure and the loop should park.
        $reachedSteadyState = $stopRequested -or ($null -ne (Get-ServerPortFromDevLog))
        if ($reachedSteadyState) {
            # FailFast / Stop-Process -Force don't yield exit 0; the
            # original "exit 0 only" rule would falsely park the loop.
            Write-LoopLog "Server stopped (exit code $exitCode). Restarting from step 1..."
        } elseif ($exitCode -ne 0) {
            $failureMessage = "Step 3 (server-run) failed to start, exit code $exitCode."
        } else {
            Write-LoopLog "Server stopped. Restarting from step 1..."
        }
    }

    if ($failureMessage) {
        Write-LoopLog $failureMessage
        Wait-ForRestart
    }
}
