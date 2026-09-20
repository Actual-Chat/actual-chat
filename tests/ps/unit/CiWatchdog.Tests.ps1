Describe "CiWatchdog.ps1" {
    BeforeAll {
        . "$PSScriptRoot/../../../scripts/CiWatchdog.ps1"

        # `gh run view --log-failed` prefixes every line; the parser has to see past it.
        function New-JobLog {
            param([string[]]$Lines)
            $prefix = "Run Slow tests / Slow tests results`tUNKNOWN STEP`t2026-09-20T12:56:27.9516423Z "
            return ($Lines | ForEach-Object { "$prefix$_" }) -join "`n"
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
    }

    Context "Get-CiFailureCategory" {
        It "reads a moved ref as garbage, not a failure" {
            Get-CiFailureCategory 'Checkout' | Should -Be 'Garbage'
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
        }

        It "treats the empty report step as noise" {
            Get-CiFailureCategory 'Report test results' | Should -Be 'Noise'
        }

        It "falls back to Unknown" {
            Get-CiFailureCategory 'Something nobody has seen' | Should -Be 'Unknown'
            Get-CiFailureCategory '' | Should -Be 'Unknown'
        }
    }

    Context "Test-CiKnownFlake" {
        It "matches a known offender by wildcard" {
            Test-CiKnownFlake 'ActualChat.Core.Server.IntegrationTests.Flows.TimerFlowTest.TwoFlowsTest' | Should -BeTrue
        }

        It "matches a single named test but not its neighbours" {
            Test-CiKnownFlake 'ActualChat.Contacts.IntegrationTests.ExternalContactsTest.UpdateExternalContactNameTest' | Should -BeTrue
            Test-CiKnownFlake 'ActualChat.Contacts.IntegrationTests.ExternalContactsTest.DeleteExternalContactTest' | Should -BeFalse
        }

        It "does not match an unrelated test" {
            Test-CiKnownFlake 'ActualChat.Users.IntegrationTests.AccountsTest.BasicTest' | Should -BeFalse
        }
    }

    Context "Get-CiFailedTests" {
        It "reports each failed test once, however often the log repeats it" {
            $tests = Get-CiFailedTests $script:oneFailureLog
            $tests.Count | Should -Be 1
            $tests[0].Name | Should -Be 'ActualChat.Core.Server.IntegrationTests.Flows.FailingThrottledUpdateFlowTest.RetryAndRecoverTest'
        }

        It "picks up the duration and the error message" {
            $tests = Get-CiFailedTests $script:oneFailureLog
            $tests[0].Duration | Should -Be '11 s'
            $tests[0].Error | Should -Be 'Expected FailingThrottledUpdateFlow.CallCounts.GetValueOrDefault(target) to be 3, but found 4.'
        }

        It "flags a known flake" {
            (Get-CiFailedTests $script:oneFailureLog)[0].KnownFlake | Should -BeTrue
        }

        It "is not confused by the assembly summary line" {
            $log = New-JobLog @('Failed!  - Failed:     1, Passed:    43, Skipped:     2, Total:    46, Duration: 1 m 24 s - X.dll (net11.0)')
            @(Get-CiFailedTests $log).Count | Should -Be 0
        }

        It "returns nothing for a log without failures" {
            @(Get-CiFailedTests '').Count | Should -Be 0
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
            $record = New-CiRunRecord $script:run @($script:testJob) $script:oneFailureLog
            $record.Jobs[0].FailedStep | Should -Be 'Run tests'
            $record.Jobs[0].Category | Should -Be 'Test'
        }

        It "keeps Tests and Totals as arrays even with a single element" {
            $record = New-CiRunRecord $script:run @($script:testJob) $script:oneFailureLog
            $json = $record | ConvertTo-Json -Depth 8 -Compress
            $json | Should -Match '"Tests":\['
            $json | Should -Match '"Totals":\['
        }

        It "survives a round trip through JSON" {
            $record = New-CiRunRecord $script:run @($script:testJob) $script:oneFailureLog
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
            $record = New-CiRunRecord $script:run @($job) $script:oneFailureLog
            @($record.Jobs[0].Tests).Count | Should -Be 0
            $record.Verdict | Should -Be 'Garbage'
        }

        It "records a run with no failed jobs" {
            $record = New-CiRunRecord $script:run @() ''
            $record.Verdict | Should -Be 'None'
            @($record.Jobs).Count | Should -Be 0
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
        It "re-runs a known flake on dev" {
            Test-CiRerunAllowed 'dev' 1 'KnownFlake' | Should -BeTrue
            Test-CiRerunAllowed 'dev' 1 'Infra' | Should -BeTrue
        }

        It "leaves other branches to their authors" {
            Test-CiRerunAllowed 'feat/something' 1 'KnownFlake' | Should -BeFalse
        }

        It "never re-runs a re-run" {
            Test-CiRerunAllowed 'dev' 2 'KnownFlake' | Should -BeFalse
        }

        It "does not re-run what a re-run cannot fix" {
            Test-CiRerunAllowed 'dev' 1 'NewFailure' | Should -BeFalse
            Test-CiRerunAllowed 'dev' 1 'Collapse' | Should -BeFalse
            Test-CiRerunAllowed 'dev' 1 'Build' | Should -BeFalse
        }
    }
}
