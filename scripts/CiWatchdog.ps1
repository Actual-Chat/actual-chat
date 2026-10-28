#!/usr/bin/env pwsh
# Classifies a red GitHub Actions run, records what it found, and performs the
# narrow set of actions that are safe to automate. Never touches source code.
#
# This file contains only function definitions. To use it:
#
#     pwsh -NoProfile -c ". ./scripts/CiWatchdog.ps1; Invoke-CiWatchdog -RunId 12345"

$script:Repo = 'Actual-Chat/actual-chat'
$script:DataBranch = 'ci-watchdog-data'
$script:JournalLabel = 'ci-watchdog'

# First match wins, so the more specific patterns come first. The patterns are
# matched case-insensitively, but they spell out every wording the workflows
# actually use — `Checkout` and `Checking out`, `for tests` and `of tests` —
# because a step that matches none of them is reported as unrecognized red.
$script:StepCategories = @(
    @{ Pattern = '^Checkout configs$'; Category = 'Infra' }
    @{ Pattern = '^Check(out|ing out)'; Category = 'Garbage' }
    @{ Pattern = '^(Initialize containers|Build OpenSearch configurator image|Configure OpenSearch ML pipeline)$'; Category = 'Infra' }
    @{ Pattern = '^Report .*test results$'; Category = 'Noise' }
    @{ Pattern = '(Run|Slow|Unit|Integration) tests'; Category = 'Test' }
    @{ Pattern = '^(Debug [Bb]uild (for|of) tests|Build image|Build app package|Build )'; Category = 'Build' }
    @{ Pattern = '^(Deploy|Upload to|Validate App)'; Category = 'Deploy' }
)

$script:FlakeLabel = 'ci-flaky'

# A job failing this many tests at once means its fixture or database went down,
# not that the tests themselves are at fault.
$script:CollapseThreshold = 5

# One error line is kept per failed test; the names are what the record is for.
$script:MaxErrorLength = 500

# The stack and the test's own output are kept so that whoever triages the failure later needs no
# job log: the GitHub tools return only its last 5000 lines, and the cloud agent cannot download
# the rest. Only the first tests of a job get them, which is all a collapsed run needs.
$script:DetailedTestLimit = 10
$script:MaxStackLines = 30
# The output keeps one line per log record; a test that starts two app hosts writes about 750.
$script:MaxOutputLength = 48000
$script:MaxOutputLineLength = 220
# A failing background loop logs the same record over and over; the test's own lines are never cut.
$script:MaxSimilarOutputLines = 5
# A host logs a heartbeat every second; one in this many is enough to show the machine's load.
$script:AliveLineStride = 10

# GitHub rejects a comment over 65536 characters, and a collapsed run fails thousands of tests.
$script:JournalTestLimit = 30

# How far past vstest's "Test host process crashed" line its "Results File" and blame lines are
# looked for. vstest prints the abort again on stderr without them, and that repeat is no new loss.
$script:HostLossLookahead = 4

# Waits before each re-download of a log that came back without its test output.
# Their sum has to fit the workflow's 10-minute limit.
$script:LogRetryDelaysSeconds = @(60, 120)

# The watchdog re-runs as this account; a human re-run carries the person's login.
$script:BotLogin = 'github-actions[bot]'
# How many earlier runs are read to find where a red streak began.
$script:RecoveryLookback = 50

function Get-CiFailureCategory {
    <#
    .SYNOPSIS
        Maps the name of a failed workflow step to what kind of red it is.
    .DESCRIPTION
        The step name alone separates most of the red without reading any logs:
        a failed `Checkout` is a ref that moved while the run was in flight, not
        a broken build.
    #>
    param([Parameter(Mandatory)][AllowEmptyString()][string]$StepName)

    foreach ($rule in $script:StepCategories) {
        if ($StepName -match $rule.Pattern) {
            return $rule.Category
        }
    }
    return 'Unknown'
}

function Get-CiFlakes {
    <#
    .SYNOPSIS
        Turns the known-flake issues into registry entries.
    .DESCRIPTION
        The pattern is whatever the title carries inside its first pair of
        backticks, so prose around it is free: "Flaky test: `*.TimerFlowTest.*`".
        A title without backticks names no test and is skipped.

        The body may add a line "Symptom: `<regex>`": then only a failure whose
        error matches it is the known flake, and any other failure of the same
        test is reported as new.
    .PARAMETER Issues
        Objects with .number, .title and .body, as `gh issue list --json` prints them.
    #>
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Issues)

    return @($Issues | ForEach-Object {
        if ($_.title -match '`(?<pattern>[^`]+)`') {
            $pattern = $Matches.pattern.Trim()
            $symptom = ''
            if ($_.body -match '(?m)^\s*Symptom:\s*`(?<symptom>[^`]+)`') {
                $symptom = $Matches.symptom.Trim()
            }
            [PSCustomObject]@{
                Issue = [int]$_.number
                Pattern = $pattern
                Symptom = $symptom
            }
        }
    })
}

function Get-CiFlakeIssues {
    <#
    .SYNOPSIS
        Issues out of what `gh issue list --json number,title,body` printed.
    .DESCRIPTION
        A zero exit code does not promise JSON — `gh` puts its deprecation and
        rate-limit warnings on the same stream we capture. Losing the registry
        costs one needless report; throwing here would cost the whole triage.
    #>
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Json)

    try {
        return @($Json | ConvertFrom-Json | ForEach-Object { $_ } | Where-Object { $_.title })
    }
    catch {
        Write-Warning "The '$script:FlakeLabel' issue list did not parse, so no test counts as a known flake: $_"
        return @()
    }
}

function Get-CiKnownFlakes {
    <#
    .SYNOPSIS
        The known-flake registry, read from the open issues that carry it.
    .DESCRIPTION
        The registry is the open `ci-flaky` issues, so a fix that closes its
        issue drops the test from the list with no separate step to forget.
        A failed lookup yields nothing: an extra report costs a reader a minute,
        while a wrongly silent re-run hides a regression for good.
    #>
    $found = & gh issue list --repo $script:Repo --label $script:FlakeLabel --state open --limit 200 `
        --json number,title,body 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Could not read the '$script:FlakeLabel' issues, so no test counts as a known flake: $found"
        return @()
    }
    return Get-CiFlakes @(Get-CiFlakeIssues ($found -join "`n"))
}

function Test-CiFlakeSymptom {
    <#
    .SYNOPSIS
        Whether a failure's error is the symptom its registry entry describes.
    .DESCRIPTION
        An entry without a symptom, or a failure whose log gave no error text,
        matches by name alone — there is nothing to compare. A symptom that is
        not a valid regex matches nothing, so the mistake surfaces as a report.
    #>
    param(
        [Parameter(Mandatory)][object]$Flake,
        [AllowEmptyString()][AllowNull()][string]$ErrorText)

    if (-not $Flake.Symptom -or -not $ErrorText) {
        return $true
    }
    try {
        return $ErrorText -match $Flake.Symptom
    }
    catch {
        Write-Warning "Symptom of #$($Flake.Issue) is not a valid regex: $($Flake.Symptom)"
        return $false
    }
}

function Find-CiFlake {
    <#
    .SYNOPSIS
        Matches one failed test against the registry.
    .DESCRIPTION
        Returns the issue the test matched by name (0 if none) and whether the
        failure is the known flake. A test matching by name with another symptom
        keeps its issue number, so the report can say which entry it slipped past.
    #>
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$TestName,
        [AllowEmptyString()][AllowNull()][string]$ErrorText,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Flakes)

    $byName = @($Flakes | Where-Object { $TestName -like $_.Pattern })
    $known = @($byName | Where-Object { Test-CiFlakeSymptom $_ $ErrorText })
    $issue = 0
    if ($known.Count -gt 0) {
        $issue = $known[0].Issue
    }
    elseif ($byName.Count -gt 0) {
        $issue = $byName[0].Issue
    }
    return [PSCustomObject]@{
        Issue = $issue
        Known = $known.Count -gt 0
    }
}

function ConvertTo-CiPlainText {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Line)

    # `gh run view --log-failed` prefixes every line with "job<TAB>step<TAB>timestamp ".
    $text = $Line -replace "`e\[[0-9;]*m", ''
    $text = $text -replace '^[^\t]*\t[^\t]*\t', ''
    return $text -replace '^\d{4}-\d{2}-\d{2}T[\d:.]+Z\s?', ''
}

function Split-CiLogByJob {
    <#
    .SYNOPSIS
        Splits one `gh run view --log-failed` dump into a text per job.
    .DESCRIPTION
        The command concatenates every failed job of the run, and the only
        thing separating them is the "job<TAB>" prefix on each line. Reading
        the whole dump per job would credit one shard's failures to all four,
        which is enough on its own to read as a collapsed fixture.

        Keys are job names exactly as the API reports them. A line with no
        prefix belongs to no job and is dropped.
    #>
    param([Parameter(Mandatory)][AllowEmptyString()][string]$LogText)

    $lines = @{}
    foreach ($line in ($LogText -split "`r?`n")) {
        if (($line -replace "`e\[[0-9;]*m", '') -notmatch '^(?<job>[^\t]*)\t[^\t]*\t') {
            continue
        }
        $job = $Matches.job
        if (-not $lines.ContainsKey($job)) {
            $lines[$job] = [System.Collections.Generic.List[string]]::new()
        }
        $lines[$job].Add($line)
    }

    $sections = @{}
    foreach ($job in $lines.Keys) {
        $sections[$job] = $lines[$job] -join "`n"
    }
    return $sections
}

function Format-CiMilliseconds {
    param([Parameter(Mandatory)][int]$Milliseconds)

    if ($Milliseconds -lt 1000) {
        return "$Milliseconds ms"
    }
    return "$([Math]::Round($Milliseconds / 1000)) s"
}

function Limit-CiText {
    param(
        [AllowEmptyString()][AllowNull()][string]$Text,
        [Parameter(Mandatory)][int]$MaxLength)

    if ($null -eq $Text -or $Text.Length -le $MaxLength) {
        return $Text
    }
    return $Text.Substring(0, $MaxLength) + '…'
}

function ConvertTo-CiTestOutput {
    <#
    .SYNOPSIS
        Condenses a test's "Standard Output Messages" to one line per log record.
    .DESCRIPTION
        A logger record takes two lines, a "[date time] level: Category[id]" header
        and its message; it becomes "time level Category: message". The message's
        own continuation lines are dropped, records that differ only in numbers
        and quoted names are kept $script:MaxSimilarOutputLines times, and only
        every $script:AliveLineStride-th host heartbeat stays. The test's own
        lines are kept as they are, marked "»". What is still over
        $script:MaxOutputLength loses its middle: the start shows how the test set
        up, the end how it failed.
    #>
    param([Parameter(Mandatory)][AllowEmptyCollection()][AllowEmptyString()][string[]]$Lines)

    $condensed = [System.Collections.Generic.List[string]]::new()
    $header = $null
    $aliveCount = 0
    foreach ($line in $Lines) {
        if ($line -match '^\s*\[\d{4}-\d\d-\d\d (?<time>[\d:.]+)[^\]]*\] (?<level>\w+): (?<category>[^\s\[]+)(\[\d+\])?\s*$') {
            if ($header) {
                $condensed.Add($header)
            }
            $category = ($Matches.category -split '\.')[-1]
            $header = "$($Matches.time) $($Matches.level) ${category}:"
            continue
        }
        if ($header) {
            $condensed.Add("$header $($line.Trim())")
            $header = $null
            continue
        }
        if ($line -match 'AppHost\[[^\]]*\]: alive: at (?<time>[\d:.]+), (?<rest>.*)$') {
            if ($aliveCount++ % $script:AliveLineStride -eq 0) {
                $condensed.Add("$($Matches.time) alive: $($Matches.rest)")
            }
            continue
        }
        if ($line -match '^\s*<[^>]+> AppHost\[') {
            $condensed.Add($line.Trim())
            continue
        }
        # What the test itself writes sits one column in, like a record's header; a record's
        # message lines sit further in, except for the text of an exception it logged.
        if ($line -match '^ \S' -and $line -notmatch '^ (--- End of|at |[\w.]+(Exception|Error)\b|Exception data:)') {
            $condensed.Add("» $($line.Trim())")
        }
    }
    if ($header) {
        $condensed.Add($header)
    }

    $kept = [System.Collections.Generic.List[string]]::new()
    $seen = @{}
    $dropped = 0
    foreach ($line in $condensed) {
        # Time, numbers and quoted names differ between records that say the same thing.
        $key = $line -replace '^[\d:.]+ ', '' -replace "'[^']*'|`"[^`"]*`"", "''" -replace '\d+', '0'
        if (-not $line.StartsWith('» ') -and ++$seen[$key] -gt $script:MaxSimilarOutputLines) {
            $dropped++
            continue
        }
        $kept.Add((Limit-CiText $line $script:MaxOutputLineLength))
    }
    if ($dropped -gt 0) {
        $kept.Add("… $dropped more records like the ones above, dropped")
    }

    $text = $kept -join "`n"
    if ($text.Length -le $script:MaxOutputLength) {
        return $text
    }
    $headLength = [int]($script:MaxOutputLength / 4)
    $tailLength = $script:MaxOutputLength - $headLength
    $head = $text.Substring(0, $text.LastIndexOf("`n", $headLength))
    $tail = $text.Substring($text.IndexOf("`n", $text.Length - $tailLength) + 1)
    $cut = $text.Length - $head.Length - $tail.Length
    return "$head`n… $cut characters cut …`n$tail"
}

function Get-CiTestDetails {
    <#
    .SYNOPSIS
        Reads the error, stack and output vstest prints under "Failed <test> [<duration>]".
    .DESCRIPTION
        Assemblies run in parallel and their lines interleave, so the section is
        read as its indented lines only: everything vstest writes for the test is
        indented, while another assembly's live output starts at the first column.
        The section ends at the next test's result or at the assembly summary.
    #>
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][AllowEmptyString()][string[]]$Lines,
        [Parameter(Mandatory)][int]$Start)

    $message = [System.Collections.Generic.List[string]]::new()
    $stack = [System.Collections.Generic.List[string]]::new()
    $output = [System.Collections.Generic.List[string]]::new()
    $part = ''
    for ($i = $Start + 1; $i -lt $Lines.Count; $i++) {
        $line = $Lines[$i]
        if ($line -match '^\s{2}(Failed|Passed|Skipped) \S' -or
            $line -match '^(Failed|Passed|Skipped)!\s' -or
            $line -match '^(Results File:|Standard error:|Test Run )') {
            break
        }
        if ($line -notmatch '^\s') {
            continue
        }

        if ($line -match '^\s+(?<part>Error Message|Stack Trace|Standard Output Messages):\s*$') {
            $part = $Matches.part
        }
        elseif ($part -eq 'Standard Output Messages') {
            $output.Add($line)
        }
        elseif (-not $line.Trim()) {
            continue
        }
        elseif ($part -eq 'Error Message') {
            $message.Add($line.Trim())
        }
        elseif ($part -eq 'Stack Trace' -and $stack.Count -lt $script:MaxStackLines) {
            $stack.Add($line.Trim())
        }
    }

    return [PSCustomObject]@{
        Error = $message -join ' '
        Stack = $stack -join "`n"
        Output = ConvertTo-CiTestOutput $output
    }
}

function Get-CiFailedTests {
    <#
    .SYNOPSIS
        Extracts failed test names, durations and error messages from a job log.
    .DESCRIPTION
        Each failure appears several times in the log (the xUnit marker, the
        VSTest summary, and again on stderr), so results are unique by name.

        Vitest names a failure "<file> > <suite> > <test>" on a FAIL line, with
        the error on the next line, and its duration on a separate progress line
        that names the test alone. The build tool prefixes every vitest line
        with its target ("e2e: "), which is cut off the error.

        The first $script:DetailedTestLimit xUnit failures also get their stack
        and condensed output (Get-CiTestDetails).
    #>
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$LogText,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Flakes)

    $lines = @($LogText -split "`r?`n" | ForEach-Object { ConvertTo-CiPlainText $_ })
    $durations = @{}
    $errors = @{}
    $order = [System.Collections.Generic.List[string]]::new()
    $vitestDurations = @{}
    $sections = @{}

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        if ($line -cmatch '^(?<prefix>.*?)\bFAIL\s+(?<name>\S+\.(?:test|spec)\.[cm]?[jt]sx?\b.*?)\s*$') {
            # A file that failed to load is printed as "<file> [ <file> ]".
            $name = $Matches.name -replace '\s+\[\s.*\]$', ''
            $prefix = $Matches.prefix.Trim()
            if (-not $order.Contains($name)) {
                $order.Add($name)
            }
            if (-not $errors.ContainsKey($name)) {
                for ($j = $i + 1; $j -lt [Math]::Min($i + 8, $lines.Count); $j++) {
                    $next = $lines[$j].Trim()
                    if ($prefix -and $next.StartsWith($prefix)) {
                        $next = $next.Substring($prefix.Length).Trim()
                    }
                    if ($next) {
                        $errors[$name] = $next
                        break
                    }
                }
            }
            continue
        }

        if ($line -match '×\s+(?<test>.+?)\s+(?<ms>\d+)ms\s*$') {
            if (-not $vitestDurations.ContainsKey($Matches.test)) {
                $vitestDurations[$Matches.test] = [int]$Matches.ms
            }
            continue
        }

        # The name is matched lazily rather than as \S+: a [Theory] case carries
        # its arguments, spaces and all, and losing the marker line loses the
        # assertion text with it.
        if ($line -match '\[xUnit\.net [\d:.]+\]\s+(?<name>.+?)\s+\[FAIL\]') {
            $name = $Matches.name
            if (-not $order.Contains($name)) {
                $order.Add($name)
            }
            if (-not $errors.ContainsKey($name)) {
                for ($j = $i + 1; $j -lt [Math]::Min($i + 8, $lines.Count); $j++) {
                    if ($lines[$j] -match '##\[error\](?<message>.+)') {
                        $errors[$name] = $Matches.message.Trim()
                        break
                    }
                }
            }
            continue
        }

        if ($line -match '\bFailed\s+(?<name>[A-Za-z][\w.+]*(?:\([^)]*\))?)\s+\[(?<duration>[^\]]+)\]') {
            $name = $Matches.name
            if (-not $order.Contains($name)) {
                $order.Add($name)
            }
            if (-not $durations.ContainsKey($name)) {
                $durations[$name] = $Matches.duration.Trim()
            }
            if ($line -match '^\s{2}Failed\s' -and -not $sections.ContainsKey($name)) {
                $sections[$name] = $i
            }
        }
    }

    $details = @{}
    foreach ($name in @($order | Where-Object { $sections.ContainsKey($_) } | Select-Object -First $script:DetailedTestLimit)) {
        $details[$name] = Get-CiTestDetails $lines $sections[$name]
        # The xUnit marker's error line sits among other assemblies' output and can be lost there.
        if (-not $errors.ContainsKey($name) -and $details[$name].Error) {
            $errors[$name] = $details[$name].Error
        }
    }

    foreach ($name in $order) {
        $test = ($name -split '\s+>\s+')[-1]
        if (-not $durations.ContainsKey($name) -and $vitestDurations.ContainsKey($test)) {
            $durations[$name] = Format-CiMilliseconds $vitestDurations[$test]
        }
    }

    return @($order | ForEach-Object {
        $flake = Find-CiFlake $_ $errors[$_] $Flakes
        [PSCustomObject]@{
            Name = $_
            Duration = $durations[$_]
            Error = Limit-CiText $errors[$_] $script:MaxErrorLength
            KnownFlake = $flake.Known
            FlakeIssue = $flake.Issue
            Stack = $details[$_].Stack
            Output = $details[$_].Output
        }
    })
}

function Get-CiAssemblyTotals {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$LogText)

    $pattern = 'Failed!\s+-\s+Failed:\s+(?<failed>\d+),\s+Passed:\s+(?<passed>\d+),\s+Skipped:\s+(?<skipped>\d+),\s+Total:\s+(?<total>\d+).*?-\s+(?<assembly>\S+\.dll)'
    # Vitest leaves out a count that is zero: "Tests  4 failed | 139 passed | 13 skipped (156)".
    $vitestPattern = '\bTests\s+(?<failed>\d+) failed(?:\s+\|\s+(?<passed>\d+) passed)?(?:\s+\|\s+(?<skipped>\d+) skipped)?.*?\((?<total>\d+)\)'
    return @($LogText -split "`r?`n" | ForEach-Object { ConvertTo-CiPlainText $_ } | ForEach-Object {
        $assembly = ''
        if ($_ -match $pattern) {
            $assembly = $Matches.assembly
        }
        elseif ($_ -match $vitestPattern) {
            $assembly = 'vitest'
        }
        if ($assembly) {
            [PSCustomObject]@{
                Assembly = $assembly
                Failed = [int]$Matches.failed
                Passed = [int]$Matches.passed
                Skipped = [int]$Matches.skipped
                Total = [int]$Matches.total
            }
        }
    })
}

function Get-CiHostLosses {
    <#
    .SYNOPSIS
        Finds the test hosts a job lost: crashed, or killed by the blame collector as hung.
    .DESCRIPTION
        vstest reports both as "Test host process crashed", and a lost host fails
        no test, so the job would otherwise carry no test names at all. A hang is
        told apart by the blame collector's inactivity message next to the abort;
        its test is the last one xUnit reported as long-running in that assembly.
    #>
    param([Parameter(Mandatory)][AllowEmptyString()][string]$LogText)

    $lines = @($LogText -split "`r?`n" | ForEach-Object { ConvertTo-CiPlainText $_ })
    $longRunning = @{}
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match "(?<source>[\w.]+): \[Long Running Test\] '(?<test>[^']+)'") {
            $longRunning[$Matches.source] = $Matches.test
            continue
        }
        if ($line -notmatch 'The active test run was aborted\. Reason: Test host process crashed') {
            continue
        }

        $assembly = ''
        $isHang = $false
        for ($j = $i + 1; $j -lt [Math]::Min($i + 1 + $script:HostLossLookahead, $lines.Count); $j++) {
            if ($lines[$j] -match 'inactivity time of .+ has elapsed') {
                $isHang = $true
            }
            elseif ($lines[$j] -match 'Results File: .*/tests/(?<assembly>[^/]+)/TestResults/') {
                $assembly = $Matches.assembly
                break
            }
        }
        if (-not $assembly) {
            continue
        }

        # xUnit names the source by its assembly, with the ActualChat. prefix the folder lacks.
        $source = @($longRunning.Keys | Where-Object { $_ -eq $assembly -or $_.EndsWith(".$assembly") })[0]
        [PSCustomObject]@{
            Assembly = $assembly
            Kind = if ($isHang) { 'Hang' } else { 'Crash' }
            Test = if ($isHang -and $source) { $longRunning[$source] } else { $null }
        }
    }
}

function Get-CiKernelFaults {
    <#
    .SYNOPSIS
        The kernel's lines on a killed or faulted process, from the job's crash diagnostics step.
    .DESCRIPTION
        Only `dmesg -T` output counts, which starts with a bracketed date: the same
        words also appear in the step's own command line.
    #>
    param([Parameter(Mandatory)][AllowEmptyString()][string]$LogText)

    return @($LogText -split "`r?`n" | ForEach-Object { ConvertTo-CiPlainText $_ } | ForEach-Object {
        if ($_ -match '^\[\w{3} \w{3} +\d+ [\d:]+ \d{4}\] (?<message>.*(traps:|segfault|killed process|out of memory|oom-kill).*)$') {
            Limit-CiText $Matches.message.Trim() $script:MaxErrorLength
        }
    })
}

function Get-CiRunVerdict {
    <#
    .SYNOPSIS
        Reduces the per-job findings of one run to a single verdict.
    .PARAMETER Jobs
        Objects with .Category, .Tests and .HostLosses, as produced by Get-CiRunRecord.
    #>
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Jobs,
        [int]$RunAttempt = 1)

    if ($Jobs.Count -eq 0) {
        return 'None'
    }
    $real = @($Jobs | Where-Object { $_.Category -notin @('Garbage', 'Noise') })
    if ($real.Count -eq 0) {
        return 'Garbage'
    }

    # The categories are reduced before the tests are read. A build that fell
    # over beside a flaky test is a broken build, and letting the flake name
    # the verdict would both re-run it and keep it out of the journal.
    $categories = @($real | ForEach-Object { $_.Category } | Sort-Object -Unique)
    if ($categories.Count -gt 1) {
        return 'Mixed'
    }
    if ($categories[0] -ne 'Test') {
        return $categories[0]
    }

    # A lost host takes every test it had not run yet with it, so the tests that
    # did fail beside it say less than the loss itself.
    $losses = @($real | ForEach-Object { $_.HostLosses } | Where-Object { $_ })
    if (@($losses | Where-Object { $_.Kind -eq 'Crash' }).Count -gt 0) {
        return 'HostCrash'
    }
    if ($losses.Count -gt 0) {
        return 'HostHang'
    }
    if (@($real | Where-Object { @($_.Tests).Count -ge $script:CollapseThreshold }).Count -gt 0) {
        return 'Collapse'
    }
    $tests = @($real | ForEach-Object { $_.Tests })
    # A missing log, or a failure printed in a format the parser doesn't know,
    # leaves a test job with no test names.
    if ($tests.Count -eq 0) {
        return 'Unparsed'
    }
    if (@($tests | Where-Object { -not $_.KnownFlake }).Count -gt 0) {
        return 'NewFailure'
    }
    # A flake does not fail twice on one commit. A known flake failing again on
    # a re-run is most likely a real break that its registry entry would hide.
    if ($RunAttempt -gt 1) {
        return 'RepeatedFlake'
    }
    return 'KnownFlake'
}

function Get-CiBranchSha {
    <#
    .SYNOPSIS
        The commit a branch currently points at, or '' if it cannot be read.
    #>
    param([Parameter(Mandatory)][string]$Branch)

    $found = & gh api "repos/$script:Repo/commits/$Branch" --jq .sha 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Could not read the tip of '$Branch': $found"
        return ''
    }
    return ($found | Select-Object -First 1).ToString().Trim()
}

function Test-CiRerunAllowed {
    <#
    .SYNOPSIS
        Decides whether re-running the failed jobs is safe and worth it.
    .DESCRIPTION
        Only on the default branch, only for red that a re-run can actually
        clear, and only on the first attempt — so a re-run never re-runs itself.

        The run must also still be testing the tip of the branch. Both CI
        workflows use `cancel-in-progress` on a group keyed by the ref, and
        whether a re-run joins that group is undocumented; requiring the tip
        removes the case that would matter either way, a re-run of stale red
        racing the run of the commit that superseded it. Stale red needs no
        re-run in the first place — a newer run is already testing newer code.

        An unreadable tip is no match, so the watchdog does nothing.
    #>
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Branch,
        [Parameter(Mandatory)][int]$RunAttempt,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Verdict,
        [Parameter(Mandatory)][AllowEmptyString()][string]$HeadSha,
        [Parameter(Mandatory)][AllowEmptyString()][string]$BranchSha)

    return $Branch -eq 'dev' -and $RunAttempt -eq 1 -and
        $HeadSha -ne '' -and $HeadSha -eq $BranchSha -and
        $Verdict -in @('KnownFlake', 'Infra')
}

function Invoke-CiGh {
    param([Parameter(Mandatory)][string[]]$GhArgs)

    $output = & gh @GhArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "gh $($GhArgs -join ' ') failed with exit code ${LASTEXITCODE}: $output"
    }
    return $output
}

function Get-CiFailedStepName {
    param([Parameter(Mandatory)][object]$Job)

    $step = @($Job.steps | Where-Object { $_.conclusion -eq 'failure' } | Sort-Object number)[0]
    if ($step) {
        return $step.name
    }
    return ''
}

function New-CiRunRecord {
    <#
    .SYNOPSIS
        Assembles the record of one red run from data already fetched.
    .PARAMETER Jobs
        The failed jobs only.
    .PARAMETER Log
        Output of `gh run view --log-failed`, or empty when no test job failed.
    #>
    param(
        [Parameter(Mandatory)][object]$Run,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Jobs,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Log,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Flakes)

    $sections = Split-CiLogByJob $Log

    $failed = @(foreach ($job in $Jobs) {
        $stepName = Get-CiFailedStepName $job
        $category = Get-CiFailureCategory $stepName

        # Both must stay arrays through ConvertTo-Json, which a single result
        # returned straight from a call would not.
        $tests = @()
        $totals = @()
        $hostLosses = @()
        $kernelFaults = @()
        if ($category -eq 'Test') {
            $jobLog = $sections[$job.name]
            if ($null -eq $jobLog) {
                Write-Warning "The log carries no section for job '$($job.name)'; its tests stay unparsed."
                $jobLog = ''
            }
            $tests = @(Get-CiFailedTests $jobLog $Flakes)
            $totals = @(Get-CiAssemblyTotals $jobLog)
            $hostLosses = @(Get-CiHostLosses $jobLog)
            $kernelFaults = @(Get-CiKernelFaults $jobLog)
        }

        [PSCustomObject]@{
            Name = $job.name
            FailedStep = $stepName
            Category = $category
            Tests = $tests
            Totals = $totals
            HostLosses = $hostLosses
            KernelFaults = $kernelFaults
            Url = $job.html_url
        }
    })

    return [PSCustomObject]@{
        RunId = [string]$Run.id
        RunAttempt = [int]$Run.run_attempt
        Workflow = $Run.name
        Branch = $Run.head_branch
        HeadSha = $Run.head_sha
        Event = $Run.event
        Title = $Run.display_title
        CreatedAt = $Run.created_at
        Url = $Run.html_url
        Jobs = $failed
        Verdict = Get-CiRunVerdict $failed ([int]$Run.run_attempt)
        # Set by Invoke-CiWatchdog once the re-run has actually been asked for.
        Rerun = $false
    }
}

function Get-CiRunRecord {
    param(
        [Parameter(Mandatory)][string]$RunId,
        [int[]]$LogRetryDelaysSeconds = $script:LogRetryDelaysSeconds)

    $run = Invoke-CiGh @('api', "repos/$script:Repo/actions/runs/$RunId") | ConvertFrom-Json
    $jobs = (Invoke-CiGh @('api', "repos/$script:Repo/actions/runs/$RunId/jobs?per_page=100") | ConvertFrom-Json).jobs
    $failed = @($jobs | Where-Object { $_.conclusion -eq 'failure' })

    $needsLog = @($failed | Where-Object { (Get-CiFailureCategory (Get-CiFailedStepName $_)) -eq 'Test' }).Count -gt 0
    if (-not $needsLog) {
        return New-CiRunRecord $run $failed '' @()
    }

    # `workflow_run: completed` arrives while GitHub is often still archiving
    # the logs. The job that finished last may 404, or worse, download fine
    # without its test output - so a test job with no failed tests, no totals
    # and no lost host is read as a log not ready yet, and fetched again. A log
    # that never completes degrades to Unparsed, which a human then reads.
    $flakes = @(Get-CiKnownFlakes)
    $record = New-CiRunRecord $run $failed (Get-CiFailedLog $RunId) $flakes
    foreach ($delay in $LogRetryDelaysSeconds) {
        $unread = @($record.Jobs | Where-Object {
            $_.Category -eq 'Test' -and @($_.Tests).Count -eq 0 -and @($_.Totals).Count -eq 0 -and
                @($_.HostLosses).Count -eq 0
        })
        if ($unread.Count -eq 0) {
            break
        }

        Write-Warning "No test output yet for $($unread.Name -join ', '); fetching the log again in $delay s."
        Start-Sleep -Seconds $delay
        $record = New-CiRunRecord $run $failed (Get-CiFailedLog $RunId) $flakes
    }
    return $record
}

function Get-CiFailedLog {
    param([Parameter(Mandatory)][string]$RunId)

    try {
        return (Invoke-CiGh @('run', 'view', $RunId, '--repo', $script:Repo, '--log-failed')) -join "`n"
    }
    catch {
        Write-Warning "Could not download the log of run ${RunId}: $_"
        return ''
    }
}

function Get-CiRecordPath {
    <#
    .SYNOPSIS
        Path of one run attempt's record on the data branch.
    .DESCRIPTION
        One file per attempt, so concurrent runs never contend for the same path.
    #>
    param([Parameter(Mandatory)][object]$Record)

    # A bare '/' in a .NET format string means "the culture's date separator",
    # which is not always a slash — hence the invariant culture.
    $month = ([datetime]$Record.CreatedAt).ToUniversalTime().ToString('yyyy/MM', [cultureinfo]::InvariantCulture)
    return "records/$month/$($Record.RunId)-$($Record.RunAttempt).json"
}

function ConvertTo-CiRecordRequest {
    <#
    .SYNOPSIS
        The body of the contents-API request that stores one record.
    #>
    param([Parameter(Mandatory)][object]$Record)

    $json = $Record | ConvertTo-Json -Depth 8 -Compress
    return [ordered]@{
        message = "ci-watchdog: $($Record.Verdict) in run $($Record.RunId)"
        content = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
        branch = $script:DataBranch
    } | ConvertTo-Json -Compress
}

function Save-CiRunRecord {
    <#
    .SYNOPSIS
        Stores the record as one file on the data branch.
    .DESCRIPTION
        A record already there is left alone — a run attempt never changes, and
        the watchdog may well see the same run twice. Returns $false only when
        the write itself failed, e.g. the data branch does not exist yet.
    #>
    param([Parameter(Mandatory)][object]$Record)

    $path = Get-CiRecordPath $Record
    & gh api "repos/$script:Repo/contents/${path}?ref=$script:DataBranch" 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        return $true
    }

    # The body goes through a file: a record of a collapsed run is far past the
    # 128 KB that Linux allows for one command-line argument.
    $file = Join-Path ([IO.Path]::GetTempPath()) "ci-watchdog-record-$($Record.RunId)-$($Record.RunAttempt).json"
    try {
        Set-Content -Path $file -Value (ConvertTo-CiRecordRequest $Record) -Encoding utf8NoBOM
        & gh api -X PUT "repos/$script:Repo/contents/$path" --input $file 2>&1 | Out-Null
        return $LASTEXITCODE -eq 0
    }
    finally {
        Remove-Item $file -ErrorAction SilentlyContinue
    }
}

function Get-CiJournalIssue {
    $found = Invoke-CiGh @('issue', 'list', '--repo', $script:Repo, '--label', $script:JournalLabel,
        '--state', 'open', '--limit', '1', '--json', 'number') | ConvertFrom-Json
    if (@($found).Count -gt 0) {
        return [int]$found[0].number
    }
    return 0
}

function Format-CiJournalNote {
    <#
    .SYNOPSIS
        Renders one record as the note that goes to the journal and the summary.
    .DESCRIPTION
        Whether the jobs were re-run is read off the record, so no caller can
        render a note that contradicts what the watchdog did.
    #>
    param([Parameter(Mandatory)][object]$Record)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("**$($Record.Verdict)** — [$($Record.Workflow) #$($Record.RunId)]($($Record.Url)) on ``$($Record.Branch)`` (``$($Record.HeadSha.Substring(0, 9))``)")
    $lines.Add('')
    $lines.Add("> $($Record.Title)")
    $lines.Add('')
    foreach ($job in $Record.Jobs) {
        $lines.Add("- **$($job.Name)** — $($job.Category), failed at ``$($job.FailedStep)``")
        foreach ($loss in @($job.HostLosses)) {
            if ($loss.Kind -eq 'Crash') {
                $lines.Add("  - test host of ``$($loss.Assembly)`` **crashed**")
            }
            else {
                $hungTest = if ($loss.Test) { ", last long-running test ``$($loss.Test)``" } else { '' }
                $lines.Add("  - test host of ``$($loss.Assembly)`` **hung** and was killed by the blame collector$hungTest")
            }
        }
        foreach ($fault in @($job.KernelFaults)) {
            $lines.Add("  - kernel: ``$fault``")
        }
        foreach ($test in @($job.Tests | Select-Object -First $script:JournalTestLimit)) {
            $flag = if ($test.KnownFlake) {
                "known flake #$($test.FlakeIssue)"
            }
            elseif ($test.FlakeIssue) {
                "**new symptom** — not the one #$($test.FlakeIssue) describes"
            }
            else {
                '**new**'
            }
            $lines.Add("  - ``$($test.Name)`` [$($test.Duration)] — $flag")
            if ($test.Error) {
                $lines.Add("    - $($test.Error)")
            }
        }
        $hidden = @($job.Tests).Count - $script:JournalTestLimit
        if ($hidden -gt 0) {
            $lines.Add("  - … and $hidden more, all of them in the stored record")
        }
    }
    if ($Record.Rerun) {
        $lines.Add('')
        $lines.Add('Failed jobs re-run automatically.')
    }
    return $lines -join "`n"
}

function Invoke-CiWatchdog {
    <#
    .SYNOPSIS
        Classifies one red run, records it, and takes the allowed actions.
    .PARAMETER DryRun
        Classify and report, but neither re-run jobs nor write anything back.
    #>
    param(
        [Parameter(Mandatory)][string]$RunId,
        [switch]$DryRun)

    $record = Get-CiRunRecord $RunId

    if ($record.Verdict -eq 'Garbage') {
        Write-Host "Run $RunId is garbage (a ref moved while it ran); nothing to do."
        return $record
    }

    # The re-run happens before the record is stored, so what is stored says
    # what was actually done rather than what was intended.
    $rerunFailed = $false
    # Read the tip only where a re-run is on the table at all: most red is on
    # somebody's feature branch, and that branch may already be gone.
    $branchSha = ''
    if ((-not $DryRun) -and $record.Branch -eq 'dev') {
        $branchSha = Get-CiBranchSha $record.Branch
    }
    if ((-not $DryRun) -and (Test-CiRerunAllowed $record.Branch $record.RunAttempt $record.Verdict `
            $record.HeadSha $branchSha)) {
        & gh run rerun $RunId --repo $script:Repo --failed 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            $record.Rerun = $true
        }
        else {
            Write-Warning "Re-run of $RunId failed."
            $rerunFailed = $true
        }
    }

    if (-not $DryRun) {
        if (-not (Save-CiRunRecord $record)) {
            Write-Warning "Could not store the record: branch '$script:DataBranch' is missing."
        }
    }

    # A re-run that could not be started leaves red nobody was told about, so
    # it is worth a note even though its verdict on its own is not.
    $notable = $record.Branch -eq 'dev' -and $record.Verdict -in @(
        'NewFailure', 'RepeatedFlake', 'HostCrash', 'HostHang', 'Collapse', 'Build', 'Deploy', 'Mixed', 'Unparsed',
        'Unknown')
    if ((-not $DryRun) -and ($notable -or $record.Rerun -or $rerunFailed)) {
        $journal = Get-CiJournalIssue
        if ($journal -eq 0) {
            Write-Warning "No open issue labelled '$script:JournalLabel'; skipping the journal note."
        }
        else {
            $note = Format-CiJournalNote $record
            $file = Join-Path ([IO.Path]::GetTempPath()) "ci-watchdog-$RunId.md"
            Set-Content -Path $file -Value $note -Encoding utf8
            Invoke-CiGh @('issue', 'comment', "$journal", '--repo', $script:Repo, '--body-file', $file) | Out-Null
            Remove-Item $file -ErrorAction SilentlyContinue
        }
    }

    Write-Host (Format-CiJournalNote $record)
    return $record
}

function Get-CiRedStreak {
    <#
    .SYNOPSIS
        The red runs a green run ends, oldest first; empty when nothing red was reported before it.
    .DESCRIPTION
        Runs are ordered by when they completed, which is the order their messages reached Voxt.
        A run counts as red when its latest attempt failed: the watchdog re-runs only first
        attempts, so a failed latest attempt was reported. Cancelled and skipped runs reported
        nothing and are passed over.

        A green re-run of the same run ends a streak too, unless the watchdog started it: then the
        failed attempt was a flake it kept quiet about.
    #>
    param(
        [Parameter(Mandatory)][object]$Run,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$EarlierRuns,
        [Parameter(Mandatory)][AllowEmptyString()][string]$PreviousAttemptConclusion)

    $completedAt = [DateTime]$Run.updated_at
    $earlier = @($EarlierRuns |
        Where-Object { $_.id -ne $Run.id -and [DateTime]$_.updated_at -lt $completedAt } |
        Where-Object { $_.conclusion -in @('success', 'failure') } |
        Sort-Object { [DateTime]$_.updated_at } -Descending)
    $streak = [Collections.Generic.List[object]]::new()
    foreach ($earlierRun in $earlier) {
        if ($earlierRun.conclusion -ne 'failure') {
            break
        }
        $streak.Insert(0, $earlierRun)
    }
    if ($Run.run_attempt -gt 1 -and $PreviousAttemptConclusion -eq 'failure' -and
            $Run.triggering_actor.login -ne $script:BotLogin) {
        $streak.Add($Run)
    }
    return , $streak.ToArray()
}

function Format-CiDuration {
    param([Parameter(Mandatory)][TimeSpan]$Duration)

    if ($Duration.TotalMinutes -lt 1) {
        return 'under a minute'
    }
    if ($Duration.TotalHours -lt 1) {
        return "$([int][Math]::Floor($Duration.TotalMinutes)) min"
    }
    if ($Duration.TotalDays -lt 1) {
        return "$([int][Math]::Floor($Duration.TotalHours)) h $($Duration.Minutes) min"
    }
    return "$([int][Math]::Floor($Duration.TotalDays)) d $($Duration.Hours) h"
}

function Format-CiRecoveryNote {
    <#
    .SYNOPSIS
        The line a recovery message adds: where the red began, how many runs and how long it lasted.
    #>
    param(
        [Parameter(Mandatory)][object]$Run,
        [Parameter(Mandatory)][object[]]$Streak)

    $first = $Streak[0]
    $count = $Streak.Count
    $runs = if ($count -eq 1) { '1 red run' } else { "$count red runs" }
    $duration = Format-CiDuration ([DateTime]$Run.updated_at - [DateTime]$first.updated_at)
    return "Red since #$($first.run_number) ($runs, $duration): $($first.html_url)"
}

function Get-CiRecoveryNote {
    <#
    .SYNOPSIS
        For a green run, the recovery line to post to Voxt, or '' when it ends no reported red.
    #>
    param([Parameter(Mandatory)][string]$RunId)

    $run = Invoke-CiGh @('api', "repos/$script:Repo/actions/runs/$RunId") | ConvertFrom-Json
    $branch = [Uri]::EscapeDataString($run.head_branch)
    $query = "branch=$branch&status=completed&per_page=$script:RecoveryLookback"
    $earlier = (Invoke-CiGh @('api', "repos/$script:Repo/actions/workflows/$($run.workflow_id)/runs?$query") |
        ConvertFrom-Json).workflow_runs
    $previousAttempt = ''
    if ($run.run_attempt -gt 1) {
        $attempt = $run.run_attempt - 1
        $previousAttempt = (Invoke-CiGh @('api', "repos/$script:Repo/actions/runs/$RunId/attempts/$attempt") |
            ConvertFrom-Json).conclusion
    }
    $streak = Get-CiRedStreak $run @($earlier) "$previousAttempt"
    if ($streak.Count -eq 0) {
        return ''
    }
    return Format-CiRecoveryNote $run $streak
}
