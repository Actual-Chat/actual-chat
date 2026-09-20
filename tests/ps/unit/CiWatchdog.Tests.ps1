Describe "CiWatchdog.ps1" {
    BeforeAll {
        . "$PSScriptRoot/../../../scripts/CiWatchdog.ps1"

        # `gh run view --log-failed` prefixes every line; the parser has to see past it.
        function New-JobLog {
            param([string[]]$Lines, [string]$Job = 'Run Slow tests / Slow tests results')
            $prefix = "$Job`tUNKNOWN STEP`t2026-09-20T12:56:27.9516423Z "
            return ($Lines | ForEach-Object { "$prefix$_" }) -join "`n"
        }

        function New-FailureLines {
            param([string]$Test)
            return @(
                "[xUnit.net 00:00:37.78]     $Test [FAIL]"
                '##[error]Expected 3, but found 4.'
                "  Failed $Test [11 s]"
            )
        }

        $script:oneFailureLog = New-JobLog @(
            '[xUnit.net 00:00:37.78]     ActualChat.Core.Server.IntegrationTests.Flows.FailingThrottledUpdateFlowTest.RetryAndRecoverTest [FAIL]'
            ''
            '##[error]Expected FailingThrottledUpdateFlow.CallCounts.GetValueOrDefault(target) to be 3, but found 4.'
            ''
            '  Failed ActualChat.Core.Server.IntegrationTests.Flows.FailingThrottledUpdateFlowTest.RetryAndRecoverTest [11 s]'
            '  Error Message:'
            '   Expected FailingThrottledUpdateFlow.CallCounts.GetValueOrDefault(target) to be 3, but found 4.'
            '  Stack Trace:'
            '     at ActualChat.Core.Server.IntegrationTests.Flows.FailingThrottledUpdateFlowTest.RetryAndRecoverTest()'
            '  Standard Output Messages:'
            ' Queues.Purge took 1.182s'
            'Failed!  - Failed:     1, Passed:    43, Skipped:     2, Total:    46, Duration: 1 m 24 s - ActualChat.Core.Server.IntegrationTests.dll (net11.0)'
            'Standard error:'
            '[xUnit.net 00:00:37.78]     ActualChat.Core.Server.IntegrationTests.Flows.FailingThrottledUpdateFlowTest.RetryAndRecoverTest [FAIL]'
        )

        # Stands in for what Get-CiKnownFlakes reads off the open ci-flaky issues.
        $script:knownPatterns = @('*.FailingThrottledUpdateFlowTest.*')
    }

    Context "Get-CiFailureCategory" {
        It "reads a moved ref as garbage, not a failure" {
            Get-CiFailureCategory 'Checkout' | Should -Be 'Garbage'
        }

        It "reads the deploy job's checkout too, whatever it is called" {
            Get-CiFailureCategory 'Checking out refs/heads/dev' | Should -Be 'Garbage'
        }

        It "keeps the configs checkout separate from the repository checkout" {
            Get-CiFailureCategory 'Checkout configs' | Should -Be 'Infra'
        }

        It "recognizes every shape of test step" {
            Get-CiFailureCategory 'Run tests' | Should -Be 'Test'
            Get-CiFailureCategory 'Run Unit tests' | Should -Be 'Test'
            Get-CiFailureCategory 'Slow tests results' | Should -Be 'Test'
            Get-CiFailureCategory 'Run Integration tests (core)' | Should -Be 'Test'
        }

        It "does not mistake the test build for a test run" {
            Get-CiFailureCategory 'Debug Build for tests' | Should -Be 'Build'
            Get-CiFailureCategory 'Debug build of tests' | Should -Be 'Build'
        }

        It "treats every report step as noise, whichever suite it reports on" {
            Get-CiFailureCategory 'Report test results' | Should -Be 'Noise'
            Get-CiFailureCategory 'Report unit test results' | Should -Be 'Noise'
            Get-CiFailureCategory 'Report TS unit test results' | Should -Be 'Noise'
            Get-CiFailureCategory 'Report E2E test results' | Should -Be 'Noise'
        }

        It "falls back to Unknown" {
            Get-CiFailureCategory 'Something nobody has seen' | Should -Be 'Unknown'
            Get-CiFailureCategory '' | Should -Be 'Unknown'
        }
    }

    Context "Get-CiFlakePatterns" {
        It "takes the pattern from the backticks, whatever prose surrounds it" {
            $titles = @(
                'Flaky test: `*.TimerFlowTest.*`'
                '`*.ExternalContactsTest.UpdateExternalContactNameTest` fails about once a week'
            )
            Get-CiFlakePatterns $titles | Should -Be @('*.TimerFlowTest.*', '*.ExternalContactsTest.UpdateExternalContactNameTest')
        }

        It "skips a title that names no test" {
            @(Get-CiFlakePatterns @('Some CI issue with no pattern in it')).Count | Should -Be 0
        }

        It "returns nothing for no issues" {
            @(Get-CiFlakePatterns @()).Count | Should -Be 0
        }
    }

    Context "Get-CiFlakeTitles" {
        It "reads the titles out of what gh printed" {
            $json = '[{"title":"Flaky test: `*.TimerFlowTest.*`"},{"title":"Another one"}]'
            Get-CiFlakeTitles $json | Should -Be @('Flaky test: `*.TimerFlowTest.*`', 'Another one')
        }

        It "gives up the registry rather than the run when gh prints a warning" {
            $titles = Get-CiFlakeTitles 'gh: warning: rate limit exceeded' -WarningAction SilentlyContinue
            @($titles).Count | Should -Be 0
        }

        It "returns nothing for an empty list" {
            @(Get-CiFlakeTitles '[]').Count | Should -Be 0
        }
    }

    Context "Split-CiLogByJob" {
        It "keeps each job's lines to itself" {
            $log = @(
                (New-JobLog @('alpha one', 'alpha two') -Job 'Run Integration tests (users)')
                (New-JobLog @('beta one') -Job 'Run Integration tests (chat)')
            ) -join "`n"

            $sections = Split-CiLogByJob $log
            $sections.Keys.Count | Should -Be 2
            $sections['Run Integration tests (users)'] | Should -Match 'alpha two'
            $sections['Run Integration tests (users)'] | Should -Not -Match 'beta one'
            $sections['Run Integration tests (chat)'] | Should -Not -Match 'alpha'
        }

        It "drops a line that belongs to no job" {
            (Split-CiLogByJob "no prefix here").Keys.Count | Should -Be 0
        }

        It "returns nothing for an empty log" {
            (Split-CiLogByJob '').Keys.Count | Should -Be 0
        }
    }

    Context "Test-CiKnownFlake" {
        BeforeAll {
            $script:patterns = @(
                '*.TimerFlowTest.*'
                '*.ExternalContactsTest.UpdateExternalContactNameTest'
            )
        }

        It "matches a known offender by wildcard" {
            Test-CiKnownFlake 'ActualChat.Core.Server.IntegrationTests.Flows.TimerFlowTest.TwoFlowsTest' $script:patterns | Should -BeTrue
        }

        It "matches a single named test but not its neighbours" {
            Test-CiKnownFlake 'ActualChat.Contacts.IntegrationTests.ExternalContactsTest.UpdateExternalContactNameTest' $script:patterns | Should -BeTrue
            Test-CiKnownFlake 'ActualChat.Contacts.IntegrationTests.ExternalContactsTest.DeleteExternalContactTest' $script:patterns | Should -BeFalse
        }

        It "does not match an unrelated test" {
            Test-CiKnownFlake 'ActualChat.Users.IntegrationTests.AccountsTest.BasicTest' $script:patterns | Should -BeFalse
        }

        It "counts nothing as known when the registry came back empty" {
            Test-CiKnownFlake 'ActualChat.Core.Server.IntegrationTests.Flows.TimerFlowTest.TwoFlowsTest' @() | Should -BeFalse
        }
    }

    Context "Get-CiFailedTests" {
        It "reports each failed test once, however often the log repeats it" {
            $tests = Get-CiFailedTests $script:oneFailureLog $script:knownPatterns
            $tests.Count | Should -Be 1
            $tests[0].Name | Should -Be 'ActualChat.Core.Server.IntegrationTests.Flows.FailingThrottledUpdateFlowTest.RetryAndRecoverTest'
        }

        It "picks up the duration and the error message" {
            $tests = Get-CiFailedTests $script:oneFailureLog $script:knownPatterns
            $tests[0].Duration | Should -Be '11 s'
            $tests[0].Error | Should -Be 'Expected FailingThrottledUpdateFlow.CallCounts.GetValueOrDefault(target) to be 3, but found 4.'
        }

        It "flags a known flake" {
            (Get-CiFailedTests $script:oneFailureLog $script:knownPatterns)[0].KnownFlake | Should -BeTrue
        }

        It "is not confused by the assembly summary line" {
            $log = New-JobLog @('Failed!  - Failed:     1, Passed:    43, Skipped:     2, Total:    46, Duration: 1 m 24 s - X.dll (net11.0)')
            @(Get-CiFailedTests $log @()).Count | Should -Be 0
        }

        It "returns nothing for a log without failures" {
            @(Get-CiFailedTests '' @()).Count | Should -Be 0
        }

        It "keeps a theory case together with its assertion text" {
            $name = 'ActualChat.Chat.IntegrationTests.ReaderTest.ReadTest(kind: "forward", count: 2)'
            $tests = Get-CiFailedTests (New-JobLog (New-FailureLines $name)) @()
            $tests.Count | Should -Be 1
            $tests[0].Name | Should -Be $name
            $tests[0].Error | Should -Be 'Expected 3, but found 4.'
        }
    }

    Context "Get-CiAssemblyTotals" {
        It "captures the denominator, not just the failures" {
            $totals = Get-CiAssemblyTotals $script:oneFailureLog
            $totals.Count | Should -Be 1
            $totals[0].Assembly | Should -Be 'ActualChat.Core.Server.IntegrationTests.dll'
            $totals[0].Failed | Should -Be 1
            $totals[0].Passed | Should -Be 43
            $totals[0].Skipped | Should -Be 2
            $totals[0].Total | Should -Be 46
        }
    }

    Context "Get-CiRunVerdict" {
        BeforeAll {
            function New-Job {
                param([string]$Category, [object[]]$Tests = @())
                return [PSCustomObject]@{ Category = $Category; Tests = $Tests }
            }
            function New-Test {
                param([string]$Name, [bool]$KnownFlake)
                return [PSCustomObject]@{ Name = $Name; KnownFlake = $KnownFlake }
            }
        }

        It "says Garbage when nothing but a moved ref failed" {
            Get-CiRunVerdict @((New-Job 'Garbage'), (New-Job 'Noise')) | Should -Be 'Garbage'
        }

        It "says KnownFlake when every failed test is on the list" {
            $job = New-Job 'Test' @((New-Test 'a.TimerFlowTest.X' $true), (New-Test 'b.ObserveTest1' $true))
            Get-CiRunVerdict @($job) | Should -Be 'KnownFlake'
        }

        It "says NewFailure as soon as one test is not on the list" {
            $job = New-Job 'Test' @((New-Test 'a.TimerFlowTest.X' $true), (New-Test 'b.BrandNewTest' $false))
            Get-CiRunVerdict @($job) | Should -Be 'NewFailure'
        }

        It "says Collapse when a whole assembly goes down" {
            $tests = 1..6 | ForEach-Object { New-Test "a.T$_" $true }
            Get-CiRunVerdict @((New-Job 'Test' $tests)) | Should -Be 'Collapse'
        }

        It "counts the collapse threshold per job, not per run" {
            $first = New-Job 'Test' @(1..3 | ForEach-Object { New-Test "a.T$_" $true })
            $second = New-Job 'Test' @(1..3 | ForEach-Object { New-Test "b.T$_" $true })
            Get-CiRunVerdict @($first, $second) | Should -Be 'KnownFlake'
        }

        It "does not let a flaky test speak for a broken build" {
            $tests = New-Job 'Test' @((New-Test 'a.TimerFlowTest.X' $true))
            Get-CiRunVerdict @((New-Job 'Build'), $tests) | Should -Be 'Mixed'
        }

        It "ignores garbage jobs sharing a run with a real failure" {
            $job = New-Job 'Test' @((New-Test 'b.BrandNewTest' $false))
            Get-CiRunVerdict @((New-Job 'Garbage'), $job) | Should -Be 'NewFailure'
        }

        It "passes a non-test category through" {
            Get-CiRunVerdict @((New-Job 'Infra')) | Should -Be 'Infra'
            Get-CiRunVerdict @((New-Job 'Infra'), (New-Job 'Build')) | Should -Be 'Mixed'
        }

        It "says Unparsed when a test job yields no test names" {
            Get-CiRunVerdict @((New-Job 'Test')) | Should -Be 'Unparsed'
        }

        It "says None when no job failed" {
            Get-CiRunVerdict @() | Should -Be 'None'
        }
    }

    Context "New-CiRunRecord" {
        BeforeAll {
            $script:run = [PSCustomObject]@{
                id = 35511089998; run_attempt = 1; name = 'Run slow tests'; head_branch = 'dev'
                head_sha = '3e9b33af854a289f8f02fea7948df315e9c2a786'; event = 'push'
                display_title = 'test(sharding): give a migrated shard a chance'
                created_at = '2026-09-20T12:36:13Z'
                html_url = 'https://github.com/Actual-Chat/actual-chat/actions/runs/35511089998'
            }
            $script:testJob = [PSCustomObject]@{
                name = 'Run Slow tests / Slow tests results'
                html_url = 'https://example.invalid/job/1'
                steps = @(
                    [PSCustomObject]@{ name = 'Checkout'; conclusion = 'success'; number = 1 }
                    [PSCustomObject]@{ name = 'Run tests'; conclusion = 'failure'; number = 2 }
                    [PSCustomObject]@{ name = 'Report test results'; conclusion = 'failure'; number = 3 }
                )
            }
        }

        It "blames the earliest failed step, not the last one" {
            $record = New-CiRunRecord $script:run @($script:testJob) $script:oneFailureLog $script:knownPatterns
            $record.Jobs[0].FailedStep | Should -Be 'Run tests'
            $record.Jobs[0].Category | Should -Be 'Test'
        }

        It "keeps Tests and Totals as arrays even with a single element" {
            $record = New-CiRunRecord $script:run @($script:testJob) $script:oneFailureLog $script:knownPatterns
            $json = $record | ConvertTo-Json -Depth 8 -Compress
            $json | Should -Match '"Tests":\['
            $json | Should -Match '"Totals":\['
        }

        It "survives a round trip through JSON" {
            $record = New-CiRunRecord $script:run @($script:testJob) $script:oneFailureLog $script:knownPatterns
            $back = $record | ConvertTo-Json -Depth 8 | ConvertFrom-Json
            @($back.Jobs).Count | Should -Be 1
            @($back.Jobs[0].Tests).Count | Should -Be 1
            $back.Verdict | Should -Be 'KnownFlake'
            $back.Branch | Should -Be 'dev'
        }

        It "does not read the log for a job that is not a test job" {
            $job = [PSCustomObject]@{
                name = 'Build image for dev'; html_url = 'https://example.invalid/job/2'
                steps = @([PSCustomObject]@{ name = 'Checkout'; conclusion = 'failure'; number = 1 })
            }
            $record = New-CiRunRecord $script:run @($job) $script:oneFailureLog $script:knownPatterns
            @($record.Jobs[0].Tests).Count | Should -Be 0
            $record.Verdict | Should -Be 'Garbage'
        }

        It "records a run with no failed jobs" {
            $record = New-CiRunRecord $script:run @() '' @()
            $record.Verdict | Should -Be 'None'
            @($record.Jobs).Count | Should -Be 0
        }

        It "does not credit one shard's failures to another" {
            $shards = 'users', 'chat'
            $jobs = @($shards | ForEach-Object {
                [PSCustomObject]@{
                    name = "Run Integration tests ($_)"
                    html_url = "https://example.invalid/job/$_"
                    steps = @([PSCustomObject]@{ name = 'Run tests'; conclusion = 'failure'; number = 1 })
                }
            })
            $log = @($shards | ForEach-Object {
                $lines = (New-FailureLines "ActualChat.$_.T1") + (New-FailureLines "ActualChat.$_.T2")
                New-JobLog $lines -Job "Run Integration tests ($_)"
            }) -join "`n"

            $record = New-CiRunRecord $script:run $jobs $log @()
            @($record.Jobs[0].Tests).Count | Should -Be 2
            @($record.Jobs[1].Tests).Count | Should -Be 2
            $record.Jobs[0].Tests.Name | Should -Not -Contain 'ActualChat.chat.T1'
            # Four failures across two shards, not one collapsed fixture.
            $record.Verdict | Should -Be 'NewFailure'
        }

        It "leaves a job unparsed when the log has no section for it" {
            $job = [PSCustomObject]@{
                name = 'Run Integration tests (mlsearch)'
                html_url = 'https://example.invalid/job/3'
                steps = @([PSCustomObject]@{ name = 'Run tests'; conclusion = 'failure'; number = 1 })
            }
            $record = New-CiRunRecord $script:run @($job) $script:oneFailureLog @() -WarningAction SilentlyContinue
            @($record.Jobs[0].Tests).Count | Should -Be 0
            $record.Verdict | Should -Be 'Unparsed'
        }
    }

    Context "Format-CiJournalNote" {
        It "mentions a re-run only when the record carries one" {
            $record = New-CiRunRecord $script:run @($script:testJob) $script:oneFailureLog $script:knownPatterns
            Format-CiJournalNote $record | Should -Not -Match 're-run automatically'
            $record.Rerun = $true
            Format-CiJournalNote $record | Should -Match 're-run automatically'
        }
    }

    Context "Get-CiRecordPath" {
        It "separates year and month with a slash in any culture" {
            $record = [PSCustomObject]@{ CreatedAt = '2026-09-20T12:36:13Z'; RunId = '35511089998'; RunAttempt = 1 }
            $culture = [Threading.Thread]::CurrentThread.CurrentCulture
            try {
                [Threading.Thread]::CurrentThread.CurrentCulture = [cultureinfo]::new('de-DE')
                Get-CiRecordPath $record | Should -Be 'records/2026/09/35511089998-1.json'
            }
            finally {
                [Threading.Thread]::CurrentThread.CurrentCulture = $culture
            }
        }

        It "keeps attempts of one run apart" {
            $first = [PSCustomObject]@{ CreatedAt = '2026-09-20T12:36:13Z'; RunId = '1'; RunAttempt = 1 }
            $second = [PSCustomObject]@{ CreatedAt = '2026-09-20T12:36:13Z'; RunId = '1'; RunAttempt = 2 }
            Get-CiRecordPath $first | Should -Not -Be (Get-CiRecordPath $second)
        }
    }

    Context "Test-CiRerunAllowed" {
        BeforeAll {
            $script:tip = '3e9b33af854a289f8f02fea7948df315e9c2a786'
        }

        It "re-runs a known flake on dev" {
            Test-CiRerunAllowed 'dev' 1 'KnownFlake' $script:tip $script:tip | Should -BeTrue
            Test-CiRerunAllowed 'dev' 1 'Infra' $script:tip $script:tip | Should -BeTrue
        }

        It "leaves other branches to their authors" {
            Test-CiRerunAllowed 'feat/something' 1 'KnownFlake' $script:tip $script:tip | Should -BeFalse
        }

        It "never re-runs a re-run" {
            Test-CiRerunAllowed 'dev' 2 'KnownFlake' $script:tip $script:tip | Should -BeFalse
        }

        It "does not re-run what a re-run cannot fix" {
            Test-CiRerunAllowed 'dev' 1 'NewFailure' $script:tip $script:tip | Should -BeFalse
            Test-CiRerunAllowed 'dev' 1 'Collapse' $script:tip $script:tip | Should -BeFalse
            Test-CiRerunAllowed 'dev' 1 'Build' $script:tip $script:tip | Should -BeFalse
        }

        It "does not re-run red that a newer commit has already superseded" {
            Test-CiRerunAllowed 'dev' 1 'KnownFlake' $script:tip 'ffffffffffffffffffffffffffffffffffffffff' |
                Should -BeFalse
        }

        It "does nothing when the tip could not be read" {
            Test-CiRerunAllowed 'dev' 1 'KnownFlake' $script:tip '' | Should -BeFalse
            Test-CiRerunAllowed 'dev' 1 'KnownFlake' '' '' | Should -BeFalse
        }
    }
}
