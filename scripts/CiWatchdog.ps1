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

# First match wins, so the more specific patterns come first.
$script:StepCategories = @(
    @{ Pattern = '^Checkout configs$'; Category = 'Infra' }
    @{ Pattern = '^Checkout'; Category = 'Garbage' }
    @{ Pattern = '^(Initialize containers|Build OpenSearch configurator image|Configure OpenSearch ML pipeline)$'; Category = 'Infra' }
    @{ Pattern = '^Report test results$'; Category = 'Noise' }
    @{ Pattern = '(Run|Slow|Unit|Integration) tests'; Category = 'Test' }
    @{ Pattern = '^(Debug Build for tests|Build image|Build app package|Build )'; Category = 'Build' }
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

function Get-CiFlakePatterns {
    <#
    .SYNOPSIS
        Extracts test-name patterns from the titles of the known-flake issues.
    .DESCRIPTION
        The pattern is whatever the title carries inside its first pair of
        backticks, so prose around it is free: "Flaky test: `*.TimerFlowTest.*`".
        A title without backticks names no test and is skipped.
    #>
    param([Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Titles)

    return @($Titles | ForEach-Object {
        if ($_ -match '`(?<pattern>[^`]+)`') {
            $Matches.pattern.Trim()
        }
    })
}

function Get-CiKnownFlakes {
    <#
    .SYNOPSIS
        The known-flake patterns, read from the open issues that carry them.
    .DESCRIPTION
        The registry is the open `ci-flaky` issues, so a fix that closes its
        issue drops the test from the list with no separate step to forget.
        A failed lookup yields nothing: an extra report costs a reader a minute,
        while a wrongly silent re-run hides a regression for good.
    #>
    $found = & gh issue list --repo $script:Repo --label $script:FlakeLabel --state open --limit 200 --json title 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Could not read the '$script:FlakeLabel' issues, so no test counts as a known flake: $found"
        return @()
    }
    return Get-CiFlakePatterns @(($found | ConvertFrom-Json).title)
}

function Test-CiKnownFlake {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$TestName,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Patterns)

    foreach ($pattern in $Patterns) {
        if ($TestName -like $pattern) {
            return $true
        }
    }
    return $false
}

function ConvertTo-CiPlainText {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Line)

    # `gh run view --log-failed` prefixes every line with "job<TAB>step<TAB>timestamp ".
    $text = $Line -replace "`e\[[0-9;]*m", ''
    $text = $text -replace '^[^\t]*\t[^\t]*\t', ''
    return $text -replace '^\d{4}-\d{2}-\d{2}T[\d:.]+Z\s?', ''
}

function Get-CiFailedTests {
    <#
    .SYNOPSIS
        Extracts failed test names, durations and error messages from a job log.
    .DESCRIPTION
        Each failure appears several times in the log (the xUnit marker, the
        VSTest summary, and again on stderr), so results are unique by name.
    #>
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$LogText,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Patterns)

    $lines = @($LogText -split "`r?`n" | ForEach-Object { ConvertTo-CiPlainText $_ })
    $durations = @{}
    $errors = @{}
    $order = [System.Collections.Generic.List[string]]::new()

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        if ($line -match '\[xUnit\.net [\d:.]+\]\s+(?<name>\S+)\s+\[FAIL\]') {
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

    return @($order | ForEach-Object {
        [PSCustomObject]@{
            Name = $_
            Duration = $durations[$_]
            Error = $errors[$_]
            KnownFlake = Test-CiKnownFlake $_ $Patterns
        }
    })
}

function Get-CiAssemblyTotals {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$LogText)

    $pattern = 'Failed!\s+-\s+Failed:\s+(?<failed>\d+),\s+Passed:\s+(?<passed>\d+),\s+Skipped:\s+(?<skipped>\d+),\s+Total:\s+(?<total>\d+).*?-\s+(?<assembly>\S+\.dll)'
    return @($LogText -split "`r?`n" | ForEach-Object { ConvertTo-CiPlainText $_ } | ForEach-Object {
        if ($_ -match $pattern) {
            [PSCustomObject]@{
                Assembly = $Matches.assembly
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
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Jobs)

    if ($Jobs.Count -eq 0) {
        return 'None'
    }
    $real = @($Jobs | Where-Object { $_.Category -notin @('Garbage', 'Noise') })
    if ($real.Count -eq 0) {
        return 'Garbage'
    }
    if (@($real | Where-Object { @($_.Tests).Count -ge $script:CollapseThreshold }).Count -gt 0) {
        return 'Collapse'
    }

    $tests = @($real | ForEach-Object { $_.Tests })
    if ($tests.Count -gt 0) {
        if (@($tests | Where-Object { -not $_.KnownFlake }).Count -eq 0) {
            return 'KnownFlake'
        }
        return 'NewFailure'
    }

    $categories = @($real | ForEach-Object { $_.Category } | Sort-Object -Unique)
    if ($categories.Count -gt 1) {
        return 'Mixed'
    }
    # TS E2E logs carry no [FAIL] markers, so a test job can yield no test names.
    if ($categories[0] -eq 'Test') {
        return 'Unparsed'
    }
    return $categories[0]
}

function Test-CiRerunAllowed {
    <#
    .SYNOPSIS
        Decides whether re-running the failed jobs is safe and worth it.
    .DESCRIPTION
        Only on the default branch, only for red that a re-run can actually
        clear, and only on the first attempt — so a re-run never re-runs itself.
    #>
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Branch,
        [Parameter(Mandatory)][int]$RunAttempt,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Verdict)

    return $Branch -eq 'dev' -and $RunAttempt -eq 1 -and $Verdict -in @('KnownFlake', 'Infra')
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
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Patterns)

    $failed = @(foreach ($job in $Jobs) {
        $stepName = Get-CiFailedStepName $job
        $category = Get-CiFailureCategory $stepName

        # Both must stay arrays through ConvertTo-Json, which a single result
        # returned straight from a call would not.
        $tests = @()
        $totals = @()
        if ($category -eq 'Test') {
            $tests = @(Get-CiFailedTests $Log $Patterns)
            $totals = @(Get-CiAssemblyTotals $Log)
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
        Verdict = Get-CiRunVerdict $failed
    }
}

function Get-CiRunRecord {
    param([Parameter(Mandatory)][string]$RunId)

    $run = Invoke-CiGh @('api', "repos/$script:Repo/actions/runs/$RunId") | ConvertFrom-Json
    $jobs = (Invoke-CiGh @('api', "repos/$script:Repo/actions/runs/$RunId/jobs?per_page=100") | ConvertFrom-Json).jobs
    $failed = @($jobs | Where-Object { $_.conclusion -eq 'failure' })

    $needsLog = @($failed | Where-Object { (Get-CiFailureCategory (Get-CiFailedStepName $_)) -eq 'Test' }).Count -gt 0
    $log = ''
    $patterns = @()
    if ($needsLog) {
        $log = (Invoke-CiGh @('run', 'view', $RunId, '--repo', $script:Repo, '--log-failed')) -join "`n"
        $patterns = @(Get-CiKnownFlakes)
    }

    return New-CiRunRecord $run $failed $log $patterns
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
    param([Parameter(Mandatory)][object]$Record, [Parameter(Mandatory)][bool]$Rerun)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("**$($Record.Verdict)** — [$($Record.Workflow) #$($Record.RunId)]($($Record.Url)) on ``$($Record.Branch)`` (``$($Record.HeadSha.Substring(0, 9))``)")
    $lines.Add('')
    $lines.Add("> $($Record.Title)")
    $lines.Add('')
    foreach ($job in $Record.Jobs) {
        $lines.Add("- **$($job.Name)** — $($job.Category), failed at ``$($job.FailedStep)``")
        foreach ($test in $job.Tests) {
            $flag = if ($test.KnownFlake) { 'known flake' } else { '**new**' }
            $lines.Add("  - ``$($test.Name)`` [$($test.Duration)] — $flag")
            if ($test.Error) {
                $lines.Add("    - $($test.Error)")
            }
        }
    }
    if ($Rerun) {
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
    $rerun = (-not $DryRun) -and (Test-CiRerunAllowed $record.Branch $record.RunAttempt $record.Verdict)

    if ($record.Verdict -eq 'Garbage') {
        Write-Host "Run $RunId is garbage (a ref moved while it ran); nothing to do."
        return $record
    }

    if (-not $DryRun) {
        if (-not (Save-CiRunRecord $record)) {
            Write-Warning "Could not store the record: branch '$script:DataBranch' is missing."
        }
    }

    if ($rerun) {
        & gh run rerun $RunId --repo $script:Repo --failed 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Re-run of $RunId failed."
            $rerun = $false
        }
    }

    $notable = $record.Branch -eq 'dev' -and $record.Verdict -in @('NewFailure', 'Collapse', 'Build', 'Mixed', 'Unparsed', 'Unknown')
    if ((-not $DryRun) -and ($notable -or $rerun)) {
        $journal = Get-CiJournalIssue
        if ($journal -eq 0) {
            Write-Warning "No open issue labelled '$script:JournalLabel'; skipping the journal note."
        }
        else {
            $note = Format-CiJournalNote $record $rerun
            $file = Join-Path ([IO.Path]::GetTempPath()) "ci-watchdog-$RunId.md"
            Set-Content -Path $file -Value $note -Encoding utf8
            Invoke-CiGh @('issue', 'comment', "$journal", '--repo', $script:Repo, '--body-file', $file) | Out-Null
            Remove-Item $file -ErrorAction SilentlyContinue
        }
    }

    Write-Host (Format-CiJournalNote $record $rerun)
    return $record
}
