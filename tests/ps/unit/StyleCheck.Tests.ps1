BeforeAll {
    $script:Script = "$PSScriptRoot/../../../.claude/hooks/style-check/style-check.mjs"
    function Invoke-StyleCheck {
        # Not $Input: PowerShell already defines $input as an automatic variable
        param([hashtable] $HookInput)
        $json = $HookInput | ConvertTo-Json -Depth 20 -Compress
        $out = $json | node $script:Script 2>$null
        [pscustomobject] @{ ExitCode = $LASTEXITCODE; Output = ($out -join "`n") }
    }
}

Describe "style-check.mjs" {
    Context "file filter" {
        It "says nothing about a .md file" {
            $r = Invoke-StyleCheck @{
                hook_event_name = 'PostToolUse'
                tool_name = 'Edit'
                tool_input = @{ file_path = 'docs/README.md' }
                tool_response = @{ originalFile = 'a' }
            }
            $r.ExitCode | Should -Be 0
            $r.Output | Should -BeNullOrEmpty
        }

        It "says nothing about a generated file" {
            $r = Invoke-StyleCheck @{
                hook_event_name = 'PostToolUse'
                tool_name = 'Edit'
                tool_input = @{ file_path = 'src/dotnet/Core/obj/Debug/Foo.cs' }
                tool_response = @{ originalFile = 'a' }
            }
            $r.ExitCode | Should -Be 0
            $r.Output | Should -BeNullOrEmpty
        }
    }

    Context "changed ranges" {
        It "reports one region for a single-line replacement" {
            $r = & node -e @'
import("./.claude/hooks/style-check/style-check.mjs").then(m => {
    const before = "a\nb\nc\nd\n";
    const after = "a\nB\nc\nd\n";
    console.log(JSON.stringify(m.changedRanges(before, after)));
});
'@
            $r | Should -Be '[{"start":2,"end":2}]'
        }

        It "reports two regions for two separate edits" {
            $r = & node -e @'
import("./.claude/hooks/style-check/style-check.mjs").then(m => {
    const before = "a\nb\nc\nd\ne\nf\ng\nh\n";
    const after = "a\nB\nc\nd\ne\nf\nG\nh\n";
    console.log(JSON.stringify(m.changedRanges(before, after)));
});
'@
            $r | Should -Be '[{"start":2,"end":2},{"start":7,"end":7}]'
        }

        It "widens and merges ranges, clamped to the file" {
            $r = & node -e @'
import("./.claude/hooks/style-check/style-check.mjs").then(m => {
    console.log(JSON.stringify(m.widenRanges([{ start: 2, end: 2 }, { start: 7, end: 7 }], 8, 3)));
});
'@
            $r | Should -Be '[{"start":1,"end":8}]'
        }
    }

    Context "mechanical rules" {
        BeforeAll {
            $script:Fixture = "$PSScriptRoot/StyleCheck/Rules.cs"
            function Get-Findings {
                param([string] $Path, [int[]] $Lines)
                $ranges = ($Lines | ForEach-Object { "{`"start`":$_,`"end`":$_}" }) -join ','
                $expr = @"
import("./.claude/hooks/style-check/style-check.mjs").then(m => {
    const text = require("fs").readFileSync(process.argv[1], "utf8");
    const ranges = m.widenRanges([$ranges], text.split("\n").length, 3);
    console.log(JSON.stringify(m.checkMechanical({ path: process.argv[1], text, ranges, limit: 120 })));
});
"@
                & node -e $expr $Path | ConvertFrom-Json
            }
        }

        It "flags a control-flow statement on the if line" {
            $findings = Get-Findings -Path $script:Fixture -Lines @(9)
            ($findings | Where-Object { $_.message -match 'own line' }).Count | Should -Be 1
        }

        It "does not flag a guard run that ends with a blank line" {
            $findings = Get-Findings -Path $script:Fixture -Lines @(11, 13)
            ($findings | Where-Object { $_.message -match 'blank line' }).Count | Should -Be 0
        }

        It "flags a method brace on the declaration line" {
            $findings = Get-Findings -Path $script:Fixture -Lines @(18)
            ($findings | Where-Object { $_.message -match 'next line' }).Count | Should -Be 1
        }

        It "flags volatile and a long attribute" {
            $findings = Get-Findings -Path $script:Fixture -Lines @(5, 22)
            ($findings | Where-Object { $_.message -match 'volatile' }).Count | Should -Be 1
            ($findings | Where-Object { $_.message -match 'attribute' }).Count | Should -Be 1
        }

        It "flags a 124-char line but not a 115-char one, and never a URL line" {
            $long = 'x' * 124
            $ok = 'y' * 115
            $url = '// see https://example.com/' + ('z' * 120)
            $text = "$long`n$ok`n$url`n"
            $path = Join-Path ([IO.Path]::GetTempPath()) 'style-check-len.cs'
            Set-Content -LiteralPath $path -Value $text -NoNewline
            try {
                $findings = Get-Findings -Path $path -Lines @(1, 2, 3)
                $findings.Count | Should -Be 1
                $findings[0].line | Should -Be 1
                $findings[0].message | Should -Match '124 chars'
            }
            finally { Remove-Item -LiteralPath $path }
        }
    }

    Context "the reviewer's copy of the guide" {
        It "is up to date with CODING_STYLE.md" {
            & node .claude/hooks/style-check/build-agents-guide.mjs --check
            $LASTEXITCODE | Should -Be 0
        }

        It "drops a marked region, and a heading it leaves empty" {
            $r = & node -e @'
import("./.claude/hooks/style-check/build-agents-guide.mjs").then(m => {
    const source = [
        "## Kept", "", "- a rule", "",
        "### Gone", "", "<!-- script-checked:begin -->", "- the script does this", "<!-- script-checked:end -->", "",
        "### Next", "", "- another rule", "",
    ].join("\n");
    console.log(JSON.stringify(m.stripScriptChecked(source).split("\n").filter(x => x)));
});
'@
            $r | Should -Be '["## Kept","- a rule","### Next","- another rule"]'
        }

        It "keeps a heading whose own body is gone but has subsections left" {
            $r = & node -e @'
import("./.claude/hooks/style-check/build-agents-guide.mjs").then(m => {
    const source = [
        "## Parent", "", "<!-- script-checked:begin -->", "- the script does this", "<!-- script-checked:end -->", "",
        "### Child", "", "- a rule", "",
    ].join("\n");
    console.log(JSON.stringify(m.stripScriptChecked(source).split("\n").filter(x => x)));
});
'@
            $r | Should -Be '["## Parent","### Child","- a rule"]'
        }
    }

    Context "the hook end to end" {
        It "reports a too-long line the edit introduced" {
            $before = "namespace Sample;`n`npublic class A`n{`n}`n"
            $long = 'x' * 130
            $after = "namespace Sample;`n`npublic class A`n{`n    // $long`n}`n"
            $path = Join-Path ([IO.Path]::GetTempPath()) 'style-check-edit.cs'
            Set-Content -LiteralPath $path -Value $after -NoNewline
            try {
                $r = Invoke-StyleCheck @{
                    hook_event_name = 'PostToolUse'
                    tool_name = 'Edit'
                    tool_input = @{ file_path = $path }
                    tool_response = @{ originalFile = $before }
                }
                $r.Output | Should -Match 'chars, max 120'
                ($r.Output | ConvertFrom-Json).hookSpecificOutput.hookEventName | Should -Be 'PostToolUse'
            }
            finally { Remove-Item -LiteralPath $path }
        }

        It "says nothing when the edit is clean" {
            $before = "namespace Sample;`n`npublic class A`n{`n}`n"
            $after = "namespace Sample;`n`npublic class A`n{`n    // short comment`n}`n"
            $path = Join-Path ([IO.Path]::GetTempPath()) 'style-check-edit-clean.cs'
            Set-Content -LiteralPath $path -Value $after -NoNewline
            try {
                $r = Invoke-StyleCheck @{
                    hook_event_name = 'PostToolUse'
                    tool_name = 'Edit'
                    tool_input = @{ file_path = $path }
                    tool_response = @{ originalFile = $before }
                }
                $r.Output | Should -BeNullOrEmpty
            }
            finally { Remove-Item -LiteralPath $path }
        }

        It "says nothing about a violation the edit did not touch" {
            $long = 'x' * 130
            $before = "namespace Sample;`n`npublic class A`n{`n    // $long`n`n`n`n`n`n}`n"
            $after = $before -replace '\}\r?\n$', "    // short`n}`n"
            $path = Join-Path ([IO.Path]::GetTempPath()) 'style-check-untouched.cs'
            Set-Content -LiteralPath $path -Value $after -NoNewline
            try {
                $r = Invoke-StyleCheck @{
                    hook_event_name = 'PostToolUse'
                    tool_name = 'Edit'
                    tool_input = @{ file_path = $path }
                    tool_response = @{ originalFile = $before }
                }
                $r.Output | Should -BeNullOrEmpty
            }
            finally { Remove-Item -LiteralPath $path }
        }
    }

    Context "ledger" {
        BeforeEach {
            $script:LedgerRoot = "tmp/style-check/test-session"
            Remove-Item -Recurse -Force $script:LedgerRoot -ErrorAction SilentlyContinue
        }

        It "stores the baseline once per file and hands it to the review" {
            $path = Join-Path ([IO.Path]::GetTempPath()) 'style-check-ledger.cs'
            Set-Content -LiteralPath $path -Value "namespace Sample;`n`npublic class A`n{`n}`n" -NoNewline
            try {
                foreach ($i in 1..2) {
                    Invoke-StyleCheck @{
                        session_id = 'test-session'
                        hook_event_name = 'PostToolUse'
                        tool_name = 'Edit'
                        tool_input = @{ file_path = $path }
                        tool_response = @{ originalFile = "namespace Sample;`n`npublic class A`n{`n    // $i`n}`n" }
                    } | Out-Null
                }
                (Get-ChildItem "$script:LedgerRoot/main/ledger/base").Count | Should -Be 1
                $ledger = Get-Content "$script:LedgerRoot/main/ledger/ledger.json" -Raw | ConvertFrom-Json
                $ledger.PSObject.Properties.Name.Count | Should -Be 1
            }
            finally { Remove-Item -LiteralPath $path }
        }

        It "drops the previous turn's entries when a new prompt starts" {
            $first = Join-Path ([IO.Path]::GetTempPath()) 'style-check-turn1.cs'
            $second = Join-Path ([IO.Path]::GetTempPath()) 'style-check-turn2.cs'
            Set-Content -LiteralPath $first -Value "namespace Sample;`n`npublic class A`n{`n}`n" -NoNewline
            Set-Content -LiteralPath $second -Value "namespace Sample;`n`npublic class B`n{`n}`n" -NoNewline
            try {
                Invoke-StyleCheck @{
                    session_id = 'test-session'; prompt_id = 'turn-1'
                    hook_event_name = 'PostToolUse'; tool_name = 'Edit'
                    tool_input = @{ file_path = $first }
                    tool_response = @{ originalFile = "namespace Sample;`n`npublic class A`n{`n    // 1`n}`n" }
                } | Out-Null
                Invoke-StyleCheck @{
                    session_id = 'test-session'; prompt_id = 'turn-2'
                    hook_event_name = 'PostToolUse'; tool_name = 'Edit'
                    tool_input = @{ file_path = $second }
                    tool_response = @{ originalFile = "namespace Sample;`n`npublic class B`n{`n    // 2`n}`n" }
                } | Out-Null

                $ledger = Get-Content "$script:LedgerRoot/main/ledger/ledger.json" -Raw | ConvertFrom-Json
                # @() matters: one property makes .Name a string, and indexing a string yields a character
                $names = @($ledger.PSObject.Properties.Name)
                $names.Count | Should -Be 1
                ($names[0] -like '*turn2.cs') | Should -BeTrue
            }
            finally { Remove-Item -LiteralPath $first, $second }
        }

        It "keeps a subagent's edits apart, and wipes every agent's ledger on a new turn" {
            $path = Join-Path ([IO.Path]::GetTempPath()) 'style-check-sub.cs'
            $other = Join-Path ([IO.Path]::GetTempPath()) 'style-check-sub-next.cs'
            Set-Content -LiteralPath $path -Value "namespace Sample;`n`npublic class A`n{`n}`n" -NoNewline
            Set-Content -LiteralPath $other -Value "namespace Sample;`n`npublic class B`n{`n}`n" -NoNewline
            try {
                foreach ($agent in @('main', 'sub-1')) {
                    $hookInput = @{
                        session_id = 'test-session'; prompt_id = 'turn-1'
                        hook_event_name = 'PostToolUse'; tool_name = 'Edit'
                        tool_input = @{ file_path = $path }
                        tool_response = @{ originalFile = "namespace Sample;`n`npublic class A`n{`n    // 1`n}`n" }
                    }
                    if ($agent -ne 'main') { $hookInput.agent_id = $agent }
                    Invoke-StyleCheck $hookInput | Out-Null
                }
                (Test-Path "$script:LedgerRoot/main/ledger/ledger.json") | Should -BeTrue
                (Test-Path "$script:LedgerRoot/sub-1/ledger/ledger.json") | Should -BeTrue

                Invoke-StyleCheck @{
                    session_id = 'test-session'; prompt_id = 'turn-2'
                    hook_event_name = 'PostToolUse'; tool_name = 'Edit'
                    tool_input = @{ file_path = $other }
                    tool_response = @{ originalFile = "namespace Sample;`n`npublic class B`n{`n    // 2`n}`n" }
                } | Out-Null

                (Test-Path "$script:LedgerRoot/sub-1") | Should -BeFalse
                $ledger = Get-Content "$script:LedgerRoot/main/ledger/ledger.json" -Raw | ConvertFrom-Json
                @($ledger.PSObject.Properties.Name).Count | Should -Be 1
            }
            finally { Remove-Item -LiteralPath $path, $other }
        }

        It "gives the reviewer the pre-edit content, and null for a file created this turn" {
            $edited = Join-Path ([IO.Path]::GetTempPath()) 'style-check-ledger-edited.cs'
            $created = Join-Path ([IO.Path]::GetTempPath()) 'style-check-ledger-created.cs'
            $before = "namespace Sample;`n`npublic class A`n{`n    // 1`n}`n"
            Set-Content -LiteralPath $edited -Value "namespace Sample;`n`npublic class A`n{`n}`n" -NoNewline
            Set-Content -LiteralPath $created -Value "namespace Sample;`n`npublic class B`n{`n}`n" -NoNewline
            try {
                Invoke-StyleCheck @{
                    session_id = 'test-session'
                    hook_event_name = 'PostToolUse'; tool_name = 'Edit'
                    tool_input = @{ file_path = $edited }
                    tool_response = @{ originalFile = $before }
                } | Out-Null
                Invoke-StyleCheck @{
                    session_id = 'test-session'
                    hook_event_name = 'PostToolUse'; tool_name = 'Write'
                    tool_input = @{ file_path = $created }
                    tool_response = @{ originalFile = '' }
                } | Out-Null

                $ledger = Get-Content "$script:LedgerRoot/main/ledger/ledger.json" -Raw | ConvertFrom-Json
                $baseName = $ledger.PSObject.Properties | Where-Object { $_.Name -like '*ledger-edited.cs' }
                $newFile = $ledger.PSObject.Properties | Where-Object { $_.Name -like '*ledger-created.cs' }
                $newFile.Value | Should -BeNullOrEmpty
                $baseline = Get-Content "$script:LedgerRoot/main/ledger/base/$($baseName.Value)" -Raw
                $baseline | Should -Be $before
            }
            finally { Remove-Item -LiteralPath $edited, $created }
        }
    }
}
