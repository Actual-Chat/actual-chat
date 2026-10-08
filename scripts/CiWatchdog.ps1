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
    #>
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$LogText,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Flakes)

    $lines = @($LogText -split "`r?`n" | ForEach-Object { ConvertTo-CiPlainText $_ })
    $durations = @{}
    $errors = @{}
    $order = [System.Collections.Generic.List[string]]::new()
    $vitestDurations = @{}

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
            Error = $errors[$_]
            KnownFlake = $flake.Known
            FlakeIssue = $flake.Issue
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

function Get-CiRunVerdict {
    <#
    .SYNOPSIS
        Reduces the per-job findings of one run to a single verdict.
    .PARAMETER Jobs
        Objects with .Category and .Tests, as produced by Get-CiRunRecord.
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
        if ($category -eq 'Test') {
            $jobLog = $sections[$job.name]
            if ($null -eq $jobLog) {
                Write-Warning "The log carries no section for job '$($job.name)'; its tests stay unparsed."
                $jobLog = ''
            }
            $tests = @(Get-CiFailedTests $jobLog $Flakes)
            $totals = @(Get-CiAssemblyTotals $jobLog)
        }

        [PSCustomObject]@{
            Name = $job.name
            FailedStep = $stepName
            Category = $category
            Tests = $tests
            Totals = $totals
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
    param([Parameter(Mandatory)][string]$RunId)

    $run = Invoke-CiGh @('api', "repos/$script:Repo/actions/runs/$RunId") | ConvertFrom-Json
    $jobs = (Invoke-CiGh @('api', "repos/$script:Repo/actions/runs/$RunId/jobs?per_page=100") | ConvertFrom-Json).jobs
    $failed = @($jobs | Where-Object { $_.conclusion -eq 'failure' })

    $needsLog = @($failed | Where-Object { (Get-CiFailureCategory (Get-CiFailedStepName $_)) -eq 'Test' }).Count -gt 0
    $log = ''
    $flakes = @()
    if ($needsLog) {
        # `workflow_run: completed` arrives while GitHub is often still
        # archiving the logs, and a download that 404s must not cost the run
        # its triage: no log degrades to Unparsed, which a human then reads.
        try {
            $log = (Invoke-CiGh @('run', 'view', $RunId, '--repo', $script:Repo, '--log-failed')) -join "`n"
        }
        catch {
            Write-Warning "Could not download the log of run ${RunId}: $_"
        }
        $flakes = @(Get-CiKnownFlakes)
    }

    return New-CiRunRecord $run $failed $log $flakes
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

    $json = $Record | ConvertTo-Json -Depth 8 -Compress
    $content = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
    & gh api -X PUT "repos/$script:Repo/contents/$path" `
        -f message="ci-watchdog: $($Record.Verdict) in run $($Record.RunId)" `
        -f content="$content" `
        -f branch="$script:DataBranch" 2>&1 | Out-Null
    return $LASTEXITCODE -eq 0
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
        foreach ($test in $job.Tests) {
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
        'NewFailure', 'RepeatedFlake', 'Collapse', 'Build', 'Deploy', 'Mixed', 'Unparsed', 'Unknown')
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
