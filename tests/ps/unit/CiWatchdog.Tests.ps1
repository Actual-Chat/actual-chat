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

        # Lines of the nightly TS E2E run 37717072265, colors and the build tool's prefix included.
        $script:vitestLog = New-JobLog -Job 'TS E2E Tests / TS E2E tests results' @(
            "`e[34me2e: `e[0m`e[31m     `e[31m×`e[31m reloads the map when a request for it stalls`e[39m`e[33m 42927`e[2mms`e[22m`e[39m"
            "`e[34me2e: `e[0m`e[31m     `e[31m×`e[31m fits the map card into a narrow screen`e[39m`e[33m 640`e[2mms`e[22m`e[39m"
            "`e[34me2e: `e[0m`e[22m`e[39mFAILED TEST SCREENSHOT: http://localhost:7080/chat -> /home/runner/tmp/e2e-failed-reloads.png"
            "`e[34me2e: `e[0m`e[31m`e[41m`e[1m FAIL `e[22m`e[49m tests/ts/e2e/map-link-preview.test.ts`e[2m > `e[22mmap link preview`e[2m > `e[22mreloads the map when a request for it stalls`e[0m"
            "`e[34me2e: `e[0m`e[31m`e[31m`e[1mTimeoutError`e[22m: page.waitForResponse: Timeout 30000ms exceeded while waiting for event `"response`"`e[39m`e[0m"
            "`e[34me2e: `e[0m`e[31m`e[36m `e[2m❯`e[22m tests/ts/e2e/map-link-preview.test.ts:`e[2m126:39`e[22m`e[39m`e[0m"
            "`e[34me2e: `e[0m`e[31m`e[41m`e[1m FAIL `e[22m`e[49m tests/ts/e2e/map-link-preview.test.ts`e[2m > `e[22mmap link preview`e[2m > `e[22mfits the map card into a narrow screen`e[0m"
            "`e[34me2e: `e[0m"
            "`e[34me2e: `e[0m`e[31mAssertionError: expected null not to be null`e[0m"
            "`e[34me2e: `e[0m`e[31m`e[41m`e[1m FAIL `e[22m`e[49m tests/ts/e2e/people-search.test.ts`e[2m > `e[22mglobal people search`e[0m"
            "`e[34me2e: `e[0m`e[31mError: Hook timed out in 120000ms.`e[0m"
            "`e[34me2e: `e[0m`e[31m`e[41m`e[1m FAIL `e[22m`e[49m tests/ts/e2e/map-link-preview.test.ts`e[2m > `e[22mmap link preview`e[2m > `e[22mreloads the map when a request for it stalls`e[0m"
            "`e[34me2e: `e[0m`e[2m Test Files `e[22m `e[1m`e[31m2 failed`e[39m`e[22m`e[2m | `e[22m`e[1m`e[32m43 passed`e[39m`e[22m`e[90m (45)`e[39m"
            "`e[34me2e: `e[0m`e[2m      Tests `e[22m `e[1m`e[31m3 failed`e[39m`e[22m`e[2m | `e[22m`e[1m`e[32m139 passed`e[39m`e[22m`e[2m | `e[22m`e[33m13 skipped`e[39m`e[90m (155)`e[39m"
            "`e[90mBuild:`e[0m `e[34me2e-tests`e[37m:`e[0m `e[31mFAILED!`e[0m `e[31mCommand execution failed because the underlying process (npm#29201) returned a non-zero exit code (1)."
        )

        function New-Flake {
            param([string]$Pattern, [string]$Symptom = '', [int]$Issue = 4645)
            return [PSCustomObject]@{ Issue = $Issue; Pattern = $Pattern; Symptom = $Symptom }
        }

        # Stands in for what Get-CiKnownFlakes reads off the open ci-flaky issues.
        $script:knownFlakes = @(New-Flake '*.FailingThrottledUpdateFlowTest.*')

        # Lines of Slow tests run 39679039694: a test host that died of a fault inside the GC.
        $script:hostCrashLog = New-JobLog @(
            'Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 36 s - ActualChat.Chat.IntegrationTests.dll (net11.0)'
            'The active test run was aborted. Reason: Test host process crashed : Process path: /usr/share/dotnet/dotnet'
            'Results File: /home/runner/work/actual-chat/actual-chat/tests/Core.Server.IntegrationTests/TestResults/Results.trx'
            ''
            'Test Run Aborted.'
            'Passed!  - Failed:     0, Passed:    47, Skipped:     1, Total:    48, Duration: 1 m 33 s - ActualChat.Core.Server.IntegrationTests.dll (net11.0)'
            'Standard error:'
            'The active test run was aborted. Reason: Test host process crashed : Process path: /usr/share/dotnet/dotnet'
            'Test Run Aborted.'
            "`e[36;1msudo dmesg -T | grep -iE 'killed process|out of memory|oom-kill|segfault|traps:' || echo `"(none)`"`e[0m"
            '--- Kernel: OOM kills and segfaults'
            '[Wed Oct 21 21:48:56 2026] traps: .NET BGC[28066] general protection fault ip:7f149ade4d32 sp:7f0b44faf830 error:0 in libcoreclr.so[1e3d32,7f149ad40000+3a7000]'
            '--- Memory'
        )

        # Lines of Integration tests run 39393703693: the blame collector killed a hung host,
        # which vstest reports as a crash too.
        $script:hungTest = 'ActualChat.Chat.UI.Blazor.IntegrationTests.LiveConversationDisplayTest.NavigatingToOwnNewEntryShouldKeepACollapsedLiveBlockCollapsed(keepsCollapsed: True)'
        $script:hostHangLog = New-JobLog @(
            "[xUnit.net 00:06:00.53] ActualChat.Chat.UI.Blazor.IntegrationTests: [Long Running Test] '$script:hungTest', Elapsed: 00:04:59"
            'The active test run was aborted. Reason: Test host process crashed : Process path: /usr/share/dotnet/dotnet'
            "Data collector 'Blame' message: The specified inactivity time of 5 minutes has elapsed. Collecting hang dumps from testhost and its child processes."
            'Results File: /home/runner/work/actual-chat/actual-chat/tests/Chat.UI.Blazor.IntegrationTests/TestResults/Results.trx'
            ''
            'Passed!  - Failed:     0, Passed:    77, Skipped:     1, Total:    78, Duration: 1 m 10 s - ActualChat.Chat.UI.Blazor.IntegrationTests.dll (net11.0)'
            'Test Run Aborted.'
        )
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

    Context "Get-CiFlakes" {
        BeforeAll {
            function New-Issue {
                param([int]$Number, [string]$Title, [string]$Body = '')
                return [PSCustomObject]@{ number = $Number; title = $Title; body = $Body }
            }
        }

        It "takes the pattern from the backticks, whatever prose surrounds it" {
            $issues = @(
                (New-Issue 4646 'Flaky test: `*.TimerFlowTest.*`')
                (New-Issue 4655 '`*.ExternalContactsTest.UpdateExternalContactNameTest` fails about once a week')
            )
            $flakes = Get-CiFlakes $issues
            $flakes.Pattern | Should -Be @('*.TimerFlowTest.*', '*.ExternalContactsTest.UpdateExternalContactNameTest')
            $flakes.Issue | Should -Be @(4646, 4655)
        }

        It "reads the symptom off its own line in the body" {
            $body = "Fails about once a week.`n`nSymptom: ``Expected .* to be empty``  `nMore prose with `inline` code."
            $flake = Get-CiFlakes @((New-Issue 4655 'Flaky test: `*.ExternalContactsTest.*`' $body))
            $flake.Symptom | Should -Be 'Expected .* to be empty'
        }

        It "leaves the symptom empty when the body has none" {
            $flake = Get-CiFlakes @((New-Issue 4646 'Flaky test: `*.TimerFlowTest.*`' 'The word Symptom: in prose, no backticks.'))
            $flake.Symptom | Should -Be ''
            $flake = Get-CiFlakes @([PSCustomObject]@{ number = 4646; title = 'Flaky test: `*.TimerFlowTest.*`'; body = $null })
            $flake.Symptom | Should -Be ''
        }

        It "skips a title that names no test" {
            @(Get-CiFlakes @((New-Issue 1 'Some CI issue with no pattern in it'))).Count | Should -Be 0
        }

        It "returns nothing for no issues" {
            @(Get-CiFlakes @()).Count | Should -Be 0
        }
    }

    Context "Get-CiFlakeIssues" {
        It "reads the issues out of what gh printed" {
            $json = '[{"number":4646,"title":"Flaky test: `*.TimerFlowTest.*`","body":"b"},{"number":1,"title":"Another one","body":""}]'
            $issues = Get-CiFlakeIssues $json
            $issues.title | Should -Be @('Flaky test: `*.TimerFlowTest.*`', 'Another one')
            $issues[0].number | Should -Be 4646
        }

        It "gives up the registry rather than the run when gh prints a warning" {
            $issues = Get-CiFlakeIssues 'gh: warning: rate limit exceeded' -WarningAction SilentlyContinue
            @($issues).Count | Should -Be 0
        }

        It "returns nothing for an empty list" {
            @(Get-CiFlakeIssues '[]').Count | Should -Be 0
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

    Context "Find-CiFlake" {
        BeforeAll {
            $script:flakes = @(
                (New-Flake '*.TimerFlowTest.*' -Issue 4646)
                (New-Flake '*.ExternalContactsTest.UpdateExternalContactNameTest' 'to be empty, but found' -Issue 4655)
            )
            $script:timerTest = 'ActualChat.Core.Server.IntegrationTests.Flows.TimerFlowTest.TwoFlowsTest'
            $script:contactTest = 'ActualChat.Contacts.IntegrationTests.ExternalContactsTest.UpdateExternalContactNameTest'
        }

        It "matches a known offender by wildcard" {
            $flake = Find-CiFlake $script:timerTest 'Expected flow.UntypedResult not to be <null>.' $script:flakes
            $flake.Known | Should -BeTrue
            $flake.Issue | Should -Be 4646
        }

        It "matches a single named test but not its neighbours" {
            (Find-CiFlake $script:contactTest '' $script:flakes).Known | Should -BeTrue
            $test = 'ActualChat.Contacts.IntegrationTests.ExternalContactsTest.DeleteExternalContactTest'
            (Find-CiFlake $test '' $script:flakes).Known | Should -BeFalse
        }

        It "does not match an unrelated test" {
            $flake = Find-CiFlake 'ActualChat.Users.IntegrationTests.AccountsTest.BasicTest' '' $script:flakes
            $flake.Known | Should -BeFalse
            $flake.Issue | Should -Be 0
        }

        It "counts nothing as known when the registry came back empty" {
            (Find-CiFlake $script:timerTest '' @()).Known | Should -BeFalse
        }

        It "knows the flake by its symptom" {
            $message = 'Expected contact1.ExternalContactName to be empty, but found "Jack Awesome Super".'
            (Find-CiFlake $script:contactTest $message $script:flakes).Known | Should -BeTrue
        }

        It "reports another symptom of a known test as new, naming the entry it slipped past" {
            $flake = Find-CiFlake $script:contactTest 'System.NullReferenceException : Object reference not set' $script:flakes
            $flake.Known | Should -BeFalse
            $flake.Issue | Should -Be 4655
        }

        It "falls back to the name when the log gave no error text" {
            (Find-CiFlake $script:contactTest '' $script:flakes).Known | Should -BeTrue
            (Find-CiFlake $script:contactTest $null $script:flakes).Known | Should -BeTrue
        }

        It "lets any entry of the test vouch for the failure" {
            $flakes = $script:flakes + @(New-Flake '*.ExternalContactsTest.*' 'TimeoutException' -Issue 4999)
            $flake = Find-CiFlake $script:contactTest 'System.TimeoutException : The operation has timed out.' $flakes
            $flake.Known | Should -BeTrue
            $flake.Issue | Should -Be 4999
        }

        It "treats a broken symptom regex as no match" {
            $flakes = @(New-Flake '*.TimerFlowTest.*' '([unclosed' -Issue 4646)
            $flake = Find-CiFlake $script:timerTest 'anything' $flakes -WarningAction SilentlyContinue
            $flake.Known | Should -BeFalse
            $flake.Issue | Should -Be 4646
        }
    }

    Context "Get-CiFailedTests" {
        It "reports each failed test once, however often the log repeats it" {
            $tests = Get-CiFailedTests $script:oneFailureLog $script:knownFlakes
            $tests.Count | Should -Be 1
            $tests[0].Name | Should -Be 'ActualChat.Core.Server.IntegrationTests.Flows.FailingThrottledUpdateFlowTest.RetryAndRecoverTest'
        }

        It "picks up the duration and the error message" {
            $tests = Get-CiFailedTests $script:oneFailureLog $script:knownFlakes
            $tests[0].Duration | Should -Be '11 s'
            $tests[0].Error | Should -Be 'Expected FailingThrottledUpdateFlow.CallCounts.GetValueOrDefault(target) to be 3, but found 4.'
        }

        It "flags a known flake and names its issue" {
            $test = (Get-CiFailedTests $script:oneFailureLog $script:knownFlakes)[0]
            $test.KnownFlake | Should -BeTrue
            $test.FlakeIssue | Should -Be 4645
        }

        It "checks the symptom against the error the log carried" {
            $flakes = @(New-Flake '*.FailingThrottledUpdateFlowTest.*' 'TimeoutException')
            $test = (Get-CiFailedTests $script:oneFailureLog $flakes)[0]
            $test.KnownFlake | Should -BeFalse
            $test.FlakeIssue | Should -Be 4645
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

        It "names a vitest failure by its file, suite and test, once each" {
            $tests = Get-CiFailedTests $script:vitestLog @()
            $tests.Name | Should -Be @(
                'tests/ts/e2e/map-link-preview.test.ts > map link preview > reloads the map when a request for it stalls'
                'tests/ts/e2e/map-link-preview.test.ts > map link preview > fits the map card into a narrow screen'
                'tests/ts/e2e/people-search.test.ts > global people search'
            )
        }

        It "takes a vitest error from the next non-empty line, without the build tool's prefix" {
            $tests = Get-CiFailedTests $script:vitestLog @()
            $tests[0].Error | Should -Be 'TimeoutError: page.waitForResponse: Timeout 30000ms exceeded while waiting for event "response"'
            $tests[1].Error | Should -Be 'AssertionError: expected null not to be null'
            $tests[2].Error | Should -Be 'Error: Hook timed out in 120000ms.'
        }

        It "finds a vitest duration on the progress line that names the test alone" {
            $tests = Get-CiFailedTests $script:vitestLog @()
            $tests[0].Duration | Should -Be '43 s'
            $tests[1].Duration | Should -Be '640 ms'
            $tests[2].Duration | Should -BeNullOrEmpty
        }

        It "matches a vitest failure against the registry" {
            $flakes = @(New-Flake '*map-link-preview.test.ts > map link preview > reloads*' 'TimeoutError' -Issue 5152)
            $tests = Get-CiFailedTests $script:vitestLog $flakes
            $tests[0].KnownFlake | Should -BeTrue
            $tests[0].FlakeIssue | Should -Be 5152
            $tests[1].KnownFlake | Should -BeFalse
        }

        It "drops the path vitest repeats after a file that failed to load" {
            $log = New-JobLog @(' FAIL  tests/ts/e2e/broken.test.ts [ tests/ts/e2e/broken.test.ts ]', 'SyntaxError: Unexpected token')
            $tests = Get-CiFailedTests $log @()
            $tests[0].Name | Should -Be 'tests/ts/e2e/broken.test.ts'
            $tests[0].Error | Should -Be 'SyntaxError: Unexpected token'
        }
    }

    Context "Get-CiTestDetails" {
        BeforeAll {
            # The shape of the ShardedComputeServiceBaseTest failures (runs 39630941518, 39040616896):
            # another assembly's live output cuts into the section, and the xUnit marker's error line
            # is not where the parser looks for it.
            $script:detailedLog = New-JobLog @(
                '[xUnit.net 00:00:35.85]     ActualChat.Core.Server.IntegrationTests.Sharding.ShardedComputeServiceBaseTest.BasicTest [FAIL]'
                '[xUnit.net 00:00:35.89] ActualChat.Core.Server.IntegrationTests: <26.011s> AppHost[13, ''flow-inbox'']: alive: at 16:12:17.827, tick 1.009s'
                '  Failed ActualChat.Core.Server.IntegrationTests.Sharding.ShardedComputeServiceBaseTest.BasicTest [32 s]'
                '  Error Message:'
                '   System.Threading.Tasks.TaskCanceledException : A task was canceled.'
                '  Stack Trace:'
                '     at ActualLab.Locking.AsyncLockSet`1.<Lock>g__CompleteAsync|12_1(Task task, Entry entry) in /_/src/ActualLab.Core/Locking/AsyncLockSet.cs:line 61'
                '[xUnit.net 00:00:42.52] ActualChat.Transcription.IntegrationTests: <41.026s> AppHost[1, ''transcription'']: alive: at 14:34:35.581'
                '   at ActualChat.Core.Server.IntegrationTests.Sharding.ShardedComputeServiceBaseTest.BasicTest() in /home/runner/work/ShardedComputeServiceBaseTest.cs:line 77'
                '  Standard Output Messages:'
                ' [2026-10-21 16:11:45.104+00:00] info: TestAppHostFactory[0]'
                "       -> NewAppHost, instance 'x-ShardedComputeServiceBaseTest'"
                " <1.187s> AppHost[2, 'x-ShardedComputeServiceBaseTest']: alive: at 16:11:46.285, tick 1.18s, machine cpu 100%"
                ' [2026-10-21 16:11:48.827+00:00] info: ActualChat.BackendServiceDefs[0]'
                '       BackendServiceDefs:'
                '       - IAccountsBackend -> AccountsBackend as Local'
                ' [2026-10-21 16:12:08.690+00:00] info: ActualChat.Sharding.ShardOwner@TestBackend[0]'
                '       Shards @ 2s8mtrp6: 111111111111'
                @(1..7 | ForEach-Object {
                    " [2026-10-21 16:12:09.27$_+00:00] fail: Microsoft.EntityFrameworkCore.Database.Connection[0]"
                    "       An error occurred using the connection to database 'ac_x_$_' on server 'tcp://localhost:5432'."
                    ' Npgsql.PostgresException (0x80004005): 3D000: database "ac_x" does not exist'
                    ' --- End of stack trace from previous location ---'
                })
                ' !!!! Shard: 6'
                ' Check, iteration 0'
                '  Passed ActualChat.Core.Server.IntegrationTests.Flows.TimerFlowTest.X [1 s]'
                ' [2026-10-21 16:12:20.000+00:00] info: NotThisTest[0]'
                '       after the section'
            )
            $script:detailed = @(Get-CiFailedTests $script:detailedLog @())[0]
        }

        It "recovers the error from the vstest section when the marker's line is lost" {
            $script:detailed.Error | Should -Be 'System.Threading.Tasks.TaskCanceledException : A task was canceled.'
        }

        It "keeps the stack, without another assembly's lines cutting into it" {
            $stack = $script:detailed.Stack -split "`n"
            $stack.Count | Should -Be 2
            $stack[0] | Should -BeLike 'at ActualLab.Locking.AsyncLockSet*'
            $stack[1] | Should -BeLike '*BasicTest() in *:line 77'
        }

        It "condenses each log record to one line with its time, level and category" {
            $script:detailed.Output | Should -Match "(?m)^16:11:45\.104 info TestAppHostFactory: -> NewAppHost, instance 'x-ShardedComputeServiceBaseTest'$"
            $script:detailed.Output | Should -Match '(?m)^16:12:08\.690 info ShardOwner@TestBackend: Shards @ 2s8mtrp6'
            $script:detailed.Output | Should -Not -Match 'IAccountsBackend'
        }

        It "keeps the test's own lines and drops the text of logged exceptions" {
            $script:detailed.Output | Should -Match '(?m)^» !!!! Shard: 6$'
            $script:detailed.Output | Should -Match '(?m)^» Check, iteration 0$'
            $script:detailed.Output | Should -Not -Match 'PostgresException|End of stack trace'
        }

        It "keeps a record repeated with other names a few times, and says how many it dropped" {
            $fails = @($script:detailed.Output -split "`n" | Where-Object { $_ -match ' fail Connection: ' })
            $fails.Count | Should -Be $script:MaxSimilarOutputLines
            $script:detailed.Output | Should -Match '… 2 more records like the ones above, dropped'
        }

        It "keeps every host heartbeat's time, but only one in ten of them" {
            $script:detailed.Output | Should -Match '(?m)^16:11:46\.285 alive: tick 1\.18s, machine cpu 100%$'
        }

        It "stops at the next test's result" {
            $script:detailed.Output | Should -Not -Match 'NotThisTest|after the section'
        }

        It "cuts the middle of an output that is still too long, keeping how it started and how it ended" {
            $lines = @(1..3000 | ForEach-Object { " step $_ of a long test" })
            $output = ConvertTo-CiTestOutput $lines
            $output.Length | Should -BeLessOrEqual ($script:MaxOutputLength + 100)
            $output | Should -Match '(?m)^» step 1 of a long test$'
            $output | Should -Match '(?m)^» step 3000 of a long test$'
            $output | Should -Match '… \d+ characters cut …'
        }

        It "details only the first tests of a job, which is all a collapsed run needs" {
            $count = $script:DetailedTestLimit + 2
            $log = New-JobLog @(1..$count | ForEach-Object {
                "  Failed ActualChat.Chat.IntegrationTests.T$_ [1 s]"
                '  Error Message:'
                "   boom $_"
                '  Stack Trace:'
                "     at T$_()"
            })
            $tests = @(Get-CiFailedTests $log @())
            $tests.Count | Should -Be $count
            $tests[0].Stack | Should -Be 'at T1()'
            $tests[-1].Stack | Should -BeNullOrEmpty
        }

        It "leaves a vitest failure without details" {
            $tests = @(Get-CiFailedTests $script:vitestLog @())
            $tests[0].Stack | Should -BeNullOrEmpty
            $tests[0].Output | Should -BeNullOrEmpty
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

        It "reads vitest's totals, not its file counts" {
            $totals = Get-CiAssemblyTotals $script:vitestLog
            $totals.Count | Should -Be 1
            $totals[0].Assembly | Should -Be 'vitest'
            $totals[0].Failed | Should -Be 3
            $totals[0].Passed | Should -Be 139
            $totals[0].Skipped | Should -Be 13
            $totals[0].Total | Should -Be 155
        }

        It "counts a count vitest left out as zero" {
            $totals = Get-CiAssemblyTotals (New-JobLog @('      Tests  2 failed (2)'))
            $totals[0].Passed | Should -Be 0
            $totals[0].Skipped | Should -Be 0
            $totals[0].Total | Should -Be 2
        }
    }

    Context "Get-CiHostLosses" {
        It "names the assembly whose host crashed, once, however often vstest repeats the abort" {
            $losses = @(Get-CiHostLosses $script:hostCrashLog)
            $losses.Count | Should -Be 1
            $losses[0].Assembly | Should -Be 'Core.Server.IntegrationTests'
            $losses[0].Kind | Should -Be 'Crash'
            $losses[0].Test | Should -BeNullOrEmpty
        }

        It "tells a host the blame collector killed as hung from a crash, and names the hung test" {
            $losses = @(Get-CiHostLosses $script:hostHangLog)
            $losses.Count | Should -Be 1
            $losses[0].Assembly | Should -Be 'Chat.UI.Blazor.IntegrationTests'
            $losses[0].Kind | Should -Be 'Hang'
            $losses[0].Test | Should -Be $script:hungTest
        }

        It "finds nothing in a log where every host finished" {
            @(Get-CiHostLosses $script:oneFailureLog).Count | Should -Be 0
        }
    }

    Context "Get-CiKernelFaults" {
        It "takes the kernel's own line, not the step's command that greps for it" {
            $faults = @(Get-CiKernelFaults $script:hostCrashLog)
            $faults.Count | Should -Be 1
            $faults[0] | Should -BeLike 'traps: .NET BGC`[28066`] general protection fault*libcoreclr.so*'
        }

        It "finds nothing when the kernel reported nothing" {
            @(Get-CiKernelFaults $script:oneFailureLog).Count | Should -Be 0
        }
    }

    Context "Get-CiRunVerdict" {
        BeforeAll {
            function New-Job {
                param([string]$Category, [object[]]$Tests = @(), [string[]]$HostLosses = @())
                $losses = @($HostLosses | ForEach-Object { [PSCustomObject]@{ Assembly = 'a.Tests'; Kind = $_ } })
                return [PSCustomObject]@{ Category = $Category; Tests = $Tests; HostLosses = $losses }
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

        It "says RepeatedFlake when a known flake fails again on a re-run" {
            $job = New-Job 'Test' @((New-Test 'a.TimerFlowTest.X' $true))
            Get-CiRunVerdict @($job) -RunAttempt 2 | Should -Be 'RepeatedFlake'
        }

        It "keeps NewFailure on a re-run when a test is not on the list" {
            $job = New-Job 'Test' @((New-Test 'a.TimerFlowTest.X' $true), (New-Test 'b.BrandNewTest' $false))
            Get-CiRunVerdict @($job) -RunAttempt 2 | Should -Be 'NewFailure'
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

        It "says HostCrash for a lost host, even beside failed tests" {
            Get-CiRunVerdict @((New-Job 'Test' -HostLosses 'Crash')) | Should -Be 'HostCrash'
            $job = New-Job 'Test' @((New-Test 'a.TimerFlowTest.X' $true)) -HostLosses 'Crash'
            Get-CiRunVerdict @($job) | Should -Be 'HostCrash'
        }

        It "says HostHang only when no host crashed outright" {
            Get-CiRunVerdict @((New-Job 'Test' -HostLosses 'Hang')) | Should -Be 'HostHang'
            Get-CiRunVerdict @((New-Job 'Test' -HostLosses 'Hang'), (New-Job 'Test' -HostLosses 'Crash')) |
                Should -Be 'HostCrash'
        }

        It "keeps a lost host out of the re-run a known flake gets" {
            Test-CiRerunAllowed 'dev' 1 'HostCrash' 'abc' 'abc' | Should -BeFalse
            Test-CiRerunAllowed 'dev' 1 'HostHang' 'abc' 'abc' | Should -BeFalse
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
            $record = New-CiRunRecord $script:run @($script:testJob) $script:oneFailureLog $script:knownFlakes
            $record.Jobs[0].FailedStep | Should -Be 'Run tests'
            $record.Jobs[0].Category | Should -Be 'Test'
        }

        It "keeps Tests and Totals as arrays even with a single element" {
            $record = New-CiRunRecord $script:run @($script:testJob) $script:oneFailureLog $script:knownFlakes
            $json = $record | ConvertTo-Json -Depth 8 -Compress
            $json | Should -Match '"Tests":\['
            $json | Should -Match '"Totals":\['
        }

        It "survives a round trip through JSON" {
            $record = New-CiRunRecord $script:run @($script:testJob) $script:oneFailureLog $script:knownFlakes
            $back = $record | ConvertTo-Json -Depth 8 | ConvertFrom-Json
            @($back.Jobs).Count | Should -Be 1
            @($back.Jobs[0].Tests).Count | Should -Be 1
            $back.Verdict | Should -Be 'KnownFlake'
            $back.Branch | Should -Be 'dev'
            $back.Jobs[0].Tests[0].FlakeIssue | Should -Be 4645
        }

        It "reads the attempt off the run when it reduces the verdict" {
            $rerun = $script:run.PSObject.Copy()
            $rerun.run_attempt = 2
            $record = New-CiRunRecord $rerun @($script:testJob) $script:oneFailureLog $script:knownFlakes
            $record.Verdict | Should -Be 'RepeatedFlake'
        }

        It "does not read the log for a job that is not a test job" {
            $job = [PSCustomObject]@{
                name = 'Build image for dev'; html_url = 'https://example.invalid/job/2'
                steps = @([PSCustomObject]@{ name = 'Checkout'; conclusion = 'failure'; number = 1 })
            }
            $record = New-CiRunRecord $script:run @($job) $script:oneFailureLog $script:knownFlakes
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

        It "gives a TS E2E run a verdict instead of Unparsed" {
            $job = [PSCustomObject]@{
                name = 'TS E2E Tests / TS E2E tests results'
                html_url = 'https://example.invalid/job/4'
                steps = @([PSCustomObject]@{ name = 'Run tests'; conclusion = 'failure'; number = 1 })
            }
            $record = New-CiRunRecord $script:run @($job) $script:vitestLog @()
            @($record.Jobs[0].Tests).Count | Should -Be 3
            $record.Jobs[0].Totals[0].Assembly | Should -Be 'vitest'
            $record.Verdict | Should -Be 'NewFailure'
        }

        It "gives a crashed host a verdict instead of Unparsed, with the kernel's word on it" {
            $record = New-CiRunRecord $script:run @($script:testJob) $script:hostCrashLog @()
            $record.Verdict | Should -Be 'HostCrash'
            $back = $record | ConvertTo-Json -Depth 8 | ConvertFrom-Json
            @($back.Jobs[0].HostLosses).Count | Should -Be 1
            $back.Jobs[0].HostLosses[0].Assembly | Should -Be 'Core.Server.IntegrationTests'
            @($back.Jobs[0].KernelFaults).Count | Should -Be 1
        }

        It "keeps HostLosses and KernelFaults as arrays even when empty or single" {
            $json = New-CiRunRecord $script:run @($script:testJob) $script:hostHangLog @() | ConvertTo-Json -Depth 8 -Compress
            $json | Should -Match '"HostLosses":\[\{'
            $json | Should -Match '"KernelFaults":\[\]'
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

    Context "Get-CiRunRecord" {
        BeforeAll {
            $script:e2eRun = [PSCustomObject]@{
                id = 39531379699; run_attempt = 1; name = 'Run nightly tests'; head_branch = 'dev'
                head_sha = '1a15629152bc4c4236c3188aec403c3c60c4dd42'; event = 'schedule'
                display_title = 'Run nightly tests'; created_at = '2026-10-21T02:08:53Z'
                html_url = 'https://github.com/Actual-Chat/actual-chat/actions/runs/39531379699'
            }
            $script:e2eJobs = [PSCustomObject]@{
                jobs = @([PSCustomObject]@{
                    name = 'TS E2E Tests / TS E2E tests results'; conclusion = 'failure'
                    html_url = 'https://example.invalid/job/5'
                    steps = @([PSCustomObject]@{ name = 'Run tests'; conclusion = 'failure'; number = 1 })
                })
            }
            # What the last job's log looks like before GitHub has archived it: it
            # downloads without an error, but the test output isn't there yet.
            $script:unfinishedLog = New-JobLog -Job 'TS E2E Tests / TS E2E tests results' @(
                '##[group]Run ./b e2e-tests'
            )

            function Set-GhResponses {
                param([string[]]$Logs)
                $script:logResponses = [System.Collections.Generic.Queue[string]]::new([string[]]$Logs)
                $script:logDownloads = 0
                Mock Get-CiKnownFlakes { @() }
                Mock Start-Sleep { }
                Mock Invoke-CiGh {
                    if ($GhArgs[0] -eq 'run') {
                        $script:logDownloads++
                        return $script:logResponses.Dequeue()
                    }
                    if ($GhArgs[1] -match '/jobs') {
                        return $script:e2eJobs | ConvertTo-Json -Depth 5
                    }
                    return $script:e2eRun | ConvertTo-Json
                }
            }
        }

        It "reads the log once when it already carries the test output" {
            Set-GhResponses @($script:vitestLog)
            $record = Get-CiRunRecord '39531379699' -LogRetryDelaysSeconds @(0, 0)
            $script:logDownloads | Should -Be 1
            $record.Verdict | Should -Be 'NewFailure'
        }

        It "says how much of the log came back when the test output is still missing" {
            Set-GhResponses @($script:unfinishedLog, $script:unfinishedLog)
            $record = Get-CiRunRecord '39531379699' -LogRetryDelaysSeconds @(0) -WarningVariable warnings `
                -WarningAction SilentlyContinue
            $script:logDownloads | Should -Be 2
            $record.Verdict | Should -Be 'Unparsed'
            $warnings[-1] | Should -BeLike 'Still no test output for TS E2E Tests / TS E2E tests results (1 lines, *'
        }

        It "fetches the log again when a test job came back without its test output" {
            Set-GhResponses @($script:unfinishedLog, $script:vitestLog)
            $record = Get-CiRunRecord '39531379699' -LogRetryDelaysSeconds @(0, 0) -WarningAction SilentlyContinue
            $script:logDownloads | Should -Be 2
            @($record.Jobs[0].Tests).Count | Should -Be 3
            $record.Verdict | Should -Be 'NewFailure'
        }

        It "takes a lost host for a complete log, not one to fetch again" {
            $crash = $script:hostCrashLog -replace 'Run Slow tests / Slow tests results', 'TS E2E Tests / TS E2E tests results'
            Set-GhResponses @($crash, $script:vitestLog)
            $record = Get-CiRunRecord '39531379699' -LogRetryDelaysSeconds @(0, 0)
            $script:logDownloads | Should -Be 1
            $record.Verdict | Should -Be 'HostCrash'
        }

        It "settles for Unparsed once the retries run out" {
            Set-GhResponses @($script:unfinishedLog, $script:unfinishedLog, $script:unfinishedLog)
            $record = Get-CiRunRecord '39531379699' -LogRetryDelaysSeconds @(0, 0) -WarningAction SilentlyContinue
            $script:logDownloads | Should -Be 3
            $record.Verdict | Should -Be 'Unparsed'
        }
    }

    Context "Format-CiJournalNote" {
        It "says whose host was lost, how, and what the kernel saw" {
            $note = Format-CiJournalNote (New-CiRunRecord $script:run @($script:testJob) $script:hostCrashLog @())
            $note | Should -Match '\*\*HostCrash\*\*'
            $note | Should -Match 'test host of `Core\.Server\.IntegrationTests` \*\*crashed\*\*'
            $note | Should -Match 'kernel: `traps: \.NET BGC'

            $note = Format-CiJournalNote (New-CiRunRecord $script:run @($script:testJob) $script:hostHangLog @())
            $note | Should -Match '\*\*hung\*\*'
            $note | Should -Match ([regex]::Escape($script:hungTest))
        }

        It "mentions a re-run only when the record carries one" {
            $record = New-CiRunRecord $script:run @($script:testJob) $script:oneFailureLog $script:knownFlakes
            Format-CiJournalNote $record | Should -Not -Match 're-run automatically'
            $record.Rerun = $true
            Format-CiJournalNote $record | Should -Match 're-run automatically'
        }

        It "names the entry a failure matched, or slipped past" {
            $record = New-CiRunRecord $script:run @($script:testJob) $script:oneFailureLog $script:knownFlakes
            Format-CiJournalNote $record | Should -Match 'known flake #4645'

            $flakes = @(New-Flake '*.FailingThrottledUpdateFlowTest.*' 'TimeoutException')
            $record = New-CiRunRecord $script:run @($script:testJob) $script:oneFailureLog $flakes
            Format-CiJournalNote $record | Should -Match 'new symptom.*#4645'
        }
    }

    Context "Large records" {
        BeforeAll {
            function New-BigRecord {
                param([int]$Tests = 6000)
                $names = 1..$Tests | ForEach-Object { "ActualChat.Chat.IntegrationTests.Collapsed.FixtureTest$_.ShouldWorkWhenTheNameIsLong(argument: $('x' * 250))" }
                $job = [PSCustomObject]@{
                    Name = 'Run Integration tests (core)'; FailedStep = 'Run Integration tests'; Category = 'Test'; Url = 'u'
                    Totals = @()
                    Tests = @($names | ForEach-Object {
                        [PSCustomObject]@{ Name = $_; Duration = '1 s'; Error = 'boom'; KnownFlake = $false; FlakeIssue = 0 }
                    })
                }
                return [PSCustomObject]@{
                    RunId = '37851453091'; RunAttempt = 1; Workflow = 'CI'; Branch = 'dev'; HeadSha = 'a' * 40
                    Event = 'push'; Title = 't'; CreatedAt = '2026-10-08T22:00:00Z'; Url = 'u'
                    Jobs = @($job); Verdict = 'Collapse'; Rerun = $false
                }
            }
        }

        It "stores a record of over 2 MB without passing it as an argument" {
            $record = New-BigRecord
            $script:putBody = $null
            $script:putArgs = $null
            Mock gh {
                $global:LASTEXITCODE = if ($args -contains 'PUT') { 0 } else { 1 }
                if ($args -contains '--input') {
                    $script:putArgs = @($args)
                    $script:putBody = Get-Content -Raw $args[($args.IndexOf('--input') + 1)]
                }
            }

            Save-CiRunRecord $record | Should -BeTrue

            $script:putBody.Length | Should -BeGreaterThan 2MB
            ($script:putArgs | Measure-Object -Property Length -Maximum).Maximum | Should -BeLessThan 1000
            $request = $script:putBody | ConvertFrom-Json
            $request.branch | Should -Be 'ci-watchdog-data'
            $stored = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($request.content)) | ConvertFrom-Json
            @($stored.Jobs[0].Tests).Count | Should -Be 6000
        }

        It "removes the request file once the record is stored" {
            $record = New-BigRecord 3
            Mock gh { $global:LASTEXITCODE = if ($args -contains 'PUT') { 0 } else { 1 } }
            Save-CiRunRecord $record | Should -BeTrue
            Test-Path (Join-Path ([IO.Path]::GetTempPath()) 'ci-watchdog-record-37851453091-1.json') | Should -BeFalse
        }

        It "reports a failed write" {
            $record = New-BigRecord 3
            Mock gh { $global:LASTEXITCODE = 1 }
            Save-CiRunRecord $record | Should -BeFalse
        }

        It "cuts an endless error line but keeps the test name" {
            $log = New-JobLog @(
                '[xUnit.net 00:00:37.78]     Some.Tests.LongErrorTest.Fails [FAIL]'
                "##[error]$('e' * 5000)"
            )
            $tests = @(Get-CiFailedTests $log @())
            $tests[0].Name | Should -Be 'Some.Tests.LongErrorTest.Fails'
            $tests[0].Error.Length | Should -BeLessOrEqual 501
        }

        It "keeps the journal note under the comment size limit" {
            $note = Format-CiJournalNote (New-BigRecord)
            $note.Length | Should -BeLessThan 65536
            $note | Should -Match 'and 5970 more'
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

    Context "Get-CiRedStreak" {
        BeforeAll {
            function New-Run {
                param(
                    [long]$Id,
                    [string]$Conclusion,
                    [string]$CompletedAt,
                    [int]$Attempt = 1,
                    [string]$TriggeredBy = 'crui3er')

                return [PSCustomObject]@{
                    id = $Id; run_number = $Id; run_attempt = $Attempt; conclusion = $Conclusion
                    updated_at = $CompletedAt; html_url = "https://github.com/runs/$Id"
                    triggering_actor = [PSCustomObject]@{ login = $TriggeredBy }
                }
            }
        }

        It "ends the reported red runs that came right before it" {
            $green = New-Run 14 'success' '2026-10-28T08:00:00Z'
            $earlier = @(
                New-Run 10 'success' '2026-10-28T01:00:00Z'
                New-Run 11 'failure' '2026-10-28T02:00:00Z'
                New-Run 12 'failure' '2026-10-28T03:00:00Z'
                New-Run 13 'failure' '2026-10-28T04:00:00Z'
            )

            $streak = Get-CiRedStreak $green $earlier ''

            $streak.id | Should -Be @(11, 12, 13)
        }

        It "says nothing after a green run" {
            $green = New-Run 12 'success' '2026-10-28T08:00:00Z'
            $earlier = @(
                New-Run 10 'failure' '2026-10-28T01:00:00Z'
                New-Run 11 'success' '2026-10-28T02:00:00Z'
            )

            Get-CiRedStreak $green $earlier '' | Should -BeNullOrEmpty
        }

        It "passes over cancelled runs, which reported nothing" {
            $green = New-Run 13 'success' '2026-10-28T08:00:00Z'
            $earlier = @(
                New-Run 11 'failure' '2026-10-28T01:00:00Z'
                New-Run 12 'cancelled' '2026-10-28T02:00:00Z'
            )

            (Get-CiRedStreak $green $earlier '').id | Should -Be @(11)
        }

        It "orders runs by when they completed, not by number" {
            $green = New-Run 13 'success' '2026-10-28T08:00:00Z'
            $earlier = @(
                New-Run 12 'success' '2026-10-28T02:00:00Z'
                New-Run 11 'failure' '2026-10-28T03:00:00Z'
            )

            (Get-CiRedStreak $green $earlier '').id | Should -Be @(11)
        }

        It "ignores runs that completed after it" {
            $green = New-Run 12 'success' '2026-10-28T08:00:00Z'
            $earlier = @(New-Run 13 'failure' '2026-10-28T09:00:00Z')

            Get-CiRedStreak $green $earlier '' | Should -BeNullOrEmpty
        }

        It "counts a person's green re-run of a red run" {
            $green = New-Run 12 'success' '2026-10-28T08:00:00Z' -Attempt 2
            $earlier = @(
                New-Run 11 'failure' '2026-10-28T01:00:00Z'
                $green
            )

            (Get-CiRedStreak $green $earlier 'failure').id | Should -Be @(11, 12)
        }

        It "stays quiet about a flake the watchdog re-ran" {
            $green = New-Run 12 'success' '2026-10-28T08:00:00Z' -Attempt 2 -TriggeredBy 'github-actions[bot]'
            $earlier = @(New-Run 11 'success' '2026-10-28T01:00:00Z')

            Get-CiRedStreak $green $earlier 'failure' | Should -BeNullOrEmpty
        }
    }

    Context "Format-CiLogShape" {
        It "gives the line count and the first and last timestamps" {
            $log = "a`tb`t2026-10-28T02:05:29.9199829Z start`nnot timed`na`tb`t2026-10-28T03:21:37.2373691Z end"
            Format-CiLogShape $log | Should -Be '3 lines, 2026-10-28T02:05:29Z to 2026-10-28T03:21:37Z'
        }

        It "says when there is no log at all" {
            Format-CiLogShape '' | Should -Be 'no log'
            Format-CiLogShape $null | Should -Be 'no log'
        }
    }

    Context "Format-CiRecoveryNote" {
        It "names the first red run, the count and how long it lasted" {
            $run = [PSCustomObject]@{ updated_at = '2026-10-28T08:10:00Z' }
            $streak = @(
                [PSCustomObject]@{ run_number = 1229; updated_at = '2026-10-28T04:00:00Z'; html_url = 'https://x/1229' }
                [PSCustomObject]@{ run_number = 1230; updated_at = '2026-10-28T05:00:00Z'; html_url = 'https://x/1230' }
            )

            Format-CiRecoveryNote $run $streak | Should -Be 'Red since #1229 (2 red runs, 4 h 10 min): https://x/1229'
        }

        It "formats durations from minutes to days" {
            Format-CiDuration ([TimeSpan]::FromSeconds(30)) | Should -Be 'under a minute'
            Format-CiDuration ([TimeSpan]::FromMinutes(25)) | Should -Be '25 min'
            Format-CiDuration ([TimeSpan]::FromHours(50.5)) | Should -Be '2 d 2 h'
        }
    }
}
