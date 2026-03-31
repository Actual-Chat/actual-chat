Describe "Common.ps1" {
    BeforeAll {
        . "$PSScriptRoot/../../scripts/Common.ps1"
    }

    Context "Set-EnvFileValue" {
        BeforeEach {
            $script:envFile = [System.IO.Path]::GetTempFileName()
        }

        AfterEach {
            Remove-Item $script:envFile -ErrorAction SilentlyContinue
        }

        It "creates entry in empty file" {
            Set-EnvFileValue -Path $script:envFile -Key "FOO" -Value "bar"
            $content = Get-Content $script:envFile -Raw
            $content | Should -Match "FOO=bar"
        }

        It "updates existing entry" {
            Set-Content $script:envFile "FOO=old`nBAR=keep"
            Set-EnvFileValue -Path $script:envFile -Key "FOO" -Value "new"
            $content = Get-Content $script:envFile -Raw
            $content | Should -Match "FOO=new"
            $content | Should -Match "BAR=keep"
        }

        It "appends to file with other entries" {
            Set-Content $script:envFile "EXISTING=value"
            Set-EnvFileValue -Path $script:envFile -Key "NEW" -Value "added"
            $content = Get-Content $script:envFile -Raw
            $content | Should -Match "EXISTING=value"
            $content | Should -Match "NEW=added"
        }

        It "creates file if it does not exist" {
            $newFile = Join-Path ([System.IO.Path]::GetTempPath()) "env-test-$(Get-Random)"
            try {
                Set-EnvFileValue -Path $newFile -Key "KEY" -Value "val"
                $newFile | Should -Exist
                Get-Content $newFile -Raw | Should -Match "KEY=val"
            } finally {
                Remove-Item $newFile -ErrorAction SilentlyContinue
            }
        }
    }

    Context "Hostname regex matching" {
        # These tests verify the regex patterns used in Update-HostEntries and
        # Remove-HostEntries correctly match exact hostnames without matching
        # subdomains (e.g. worktree domains like 111222-feature.local.voxt.ai).

        BeforeAll {
            # The pattern used in Update-HostEntries line 163 for detecting existing entries
            function Test-HostnameDetected {
                param([string]$Line, [string]$Hostname)
                $escaped = [regex]::Escape($Hostname)
                return ($Line -match "(?<=\s)$escaped(?=\s|$)")
            }

            # The pattern used in Update-HostEntries line 185 for building removal filter
            function Test-HostnameRemovalFilter {
                param([string[]]$Lines, [string[]]$Hostnames)
                $patterns = ($Hostnames | ForEach-Object { [regex]::Escape($_) }) -join '|'
                return $Lines | Where-Object { $_ -notmatch "(?<=\s)($patterns)(?=\s|$)" }
            }

            # The pattern used in Remove-HostEntries line 244
            function Test-HostnameRemoveMatch {
                param([string]$Line, [string]$Hostname)
                return ($Line -match "(?<=\s)$([regex]::Escape($Hostname))(?=\s|$)")
            }
        }

        It "matches exact hostname" {
            Test-HostnameDetected "192.168.1.65  local.voxt.ai" "local.voxt.ai" | Should -BeTrue
        }

        It "matches hostname with trailing whitespace" {
            Test-HostnameDetected "192.168.1.65  local.voxt.ai  " "local.voxt.ai" | Should -BeTrue
        }

        It "matches hostname on a multi-hostname line" {
            Test-HostnameDetected "192.168.1.65  local.voxt.ai media.local.voxt.ai" "local.voxt.ai" | Should -BeTrue
            Test-HostnameDetected "192.168.1.65  local.voxt.ai media.local.voxt.ai" "media.local.voxt.ai" | Should -BeTrue
        }

        It "does NOT match subdomain (worktree domain)" {
            Test-HostnameDetected "192.168.1.65  111222-feature.local.voxt.ai" "local.voxt.ai" | Should -BeFalse
        }

        It "does NOT match longer suffix domain" {
            Test-HostnameDetected "192.168.1.65  other.local.voxt.ai" "local.voxt.ai" | Should -BeFalse
        }

        It "does NOT match hostname as prefix of longer name" {
            Test-HostnameDetected "192.168.1.65  local.voxt.ai.extra" "local.voxt.ai" | Should -BeFalse
        }

        It "matches media subdomain exactly" {
            Test-HostnameDetected "192.168.1.65  media.local.voxt.ai" "media.local.voxt.ai" | Should -BeTrue
        }

        It "does NOT match media subdomain when looking for base domain" {
            Test-HostnameDetected "192.168.1.65  media.local.voxt.ai" "local.voxt.ai" | Should -BeFalse
        }

        It "does NOT match worktree media subdomain" {
            Test-HostnameDetected "192.168.1.65  media.111222-feature.local.voxt.ai" "media.local.voxt.ai" | Should -BeFalse
        }

        It "removal filter preserves worktree entries" {
            $lines = @(
                "192.168.1.65  local.voxt.ai"
                "192.168.1.65  111222-feature.local.voxt.ai"
                "192.168.1.65  media.local.voxt.ai"
                "192.168.1.65  media.111222-feature.local.voxt.ai"
                "192.168.1.65  cdn.local.voxt.ai"
            )
            $kept = @(Test-HostnameRemovalFilter $lines @("local.voxt.ai", "media.local.voxt.ai", "cdn.local.voxt.ai"))
            $kept | Should -HaveCount 2
            $kept | Should -Contain "192.168.1.65  111222-feature.local.voxt.ai"
            $kept | Should -Contain "192.168.1.65  media.111222-feature.local.voxt.ai"
        }

        It "removal filter preserves unrelated entries" {
            $lines = @(
                "127.0.0.1  localhost"
                "192.168.1.65  local.voxt.ai"
                "::1  localhost"
            )
            $kept = @(Test-HostnameRemovalFilter $lines @("local.voxt.ai"))
            $kept | Should -HaveCount 2
            $kept | Should -Contain "127.0.0.1  localhost"
            $kept | Should -Contain "::1  localhost"
        }

        It "removal filter removes all target hostnames" {
            $lines = @(
                "192.168.1.65  local.voxt.ai"
                "192.168.1.65  media.local.voxt.ai"
                "192.168.1.65  cdn.local.voxt.ai"
            )
            $kept = @(Test-HostnameRemovalFilter $lines @("local.voxt.ai", "media.local.voxt.ai", "cdn.local.voxt.ai"))
            $kept | Should -HaveCount 0
        }

        It "Remove-HostEntries match does NOT match subdomains" {
            Test-HostnameRemoveMatch "192.168.1.65  111222-feature.local.voxt.ai" "local.voxt.ai" | Should -BeFalse
            Test-HostnameRemoveMatch "192.168.1.65  local.voxt.ai" "local.voxt.ai" | Should -BeTrue
        }

        It "handles actual.chat domains the same way" {
            Test-HostnameDetected "192.168.1.65  local.actual.chat" "local.actual.chat" | Should -BeTrue
            Test-HostnameDetected "192.168.1.65  111222-feature.local.actual.chat" "local.actual.chat" | Should -BeFalse
        }
    }

    Context "Update-HostEntries" {
        BeforeEach {
            $script:hostsFile = [System.IO.Path]::GetTempFileName()
            Mock Get-HostsFilePath { $script:hostsFile }
            Mock Get-CurrentOS { "Windows" }
        }

        AfterEach {
            Remove-Item $script:hostsFile -ErrorAction SilentlyContinue
        }

        It "adds new entries to empty file" {
            Set-Content $script:hostsFile ""
            Update-HostEntries -Hostnames "local.voxt.ai","media.local.voxt.ai" -IP "192.168.1.65"
            $content = Get-Content $script:hostsFile
            $content | Should -Contain "192.168.1.65  local.voxt.ai"
            $content | Should -Contain "192.168.1.65  media.local.voxt.ai"
        }

        It "returns the IP address" {
            Set-Content $script:hostsFile ""
            $result = Update-HostEntries -Hostnames "local.voxt.ai" -IP "10.0.0.1"
            $result | Should -Be "10.0.0.1"
        }

        It "defaults IP to 127.0.0.1" {
            Set-Content $script:hostsFile ""
            Update-HostEntries -Hostnames "local.voxt.ai"
            Get-Content $script:hostsFile | Should -Contain "127.0.0.1  local.voxt.ai"
        }

        It "skips entries that already exist with same IP" {
            Set-Content $script:hostsFile "192.168.1.65  local.voxt.ai"
            $result = Update-HostEntries -Hostnames "local.voxt.ai" -IP "192.168.1.65"
            $result | Should -Be "192.168.1.65"
            # File should be unchanged
            @(Get-Content $script:hostsFile) | Should -HaveCount 1
        }

        It "updates entries when IP changes" {
            Set-Content $script:hostsFile "10.0.0.1  local.voxt.ai"
            Update-HostEntries -Hostnames "local.voxt.ai" -IP "192.168.1.65"
            $content = Get-Content $script:hostsFile
            $content | Should -Contain "192.168.1.65  local.voxt.ai"
            $content | Should -Not -Contain "10.0.0.1  local.voxt.ai"
        }

        It "preserves worktree entries when updating base domains" {
            Set-Content $script:hostsFile @(
                "10.0.0.1  local.voxt.ai"
                "10.0.0.1  111222-feature.local.voxt.ai"
                "10.0.0.1  media.local.voxt.ai"
                "10.0.0.1  media.111222-feature.local.voxt.ai"
            )
            Update-HostEntries -Hostnames "local.voxt.ai","media.local.voxt.ai" -IP "192.168.1.65"
            $content = Get-Content $script:hostsFile
            $content | Should -Contain "192.168.1.65  local.voxt.ai"
            $content | Should -Contain "192.168.1.65  media.local.voxt.ai"
            $content | Should -Contain "10.0.0.1  111222-feature.local.voxt.ai"
            $content | Should -Contain "10.0.0.1  media.111222-feature.local.voxt.ai"
        }

        It "preserves unrelated entries" {
            Set-Content $script:hostsFile @(
                "127.0.0.1  localhost"
                "::1  localhost"
            )
            Update-HostEntries -Hostnames "local.voxt.ai" -IP "192.168.1.65"
            $content = Get-Content $script:hostsFile
            $content | Should -Contain "127.0.0.1  localhost"
            $content | Should -Contain "::1  localhost"
            $content | Should -Contain "192.168.1.65  local.voxt.ai"
        }
    }

    Context "Remove-HostEntries" {
        BeforeEach {
            $script:hostsFile = [System.IO.Path]::GetTempFileName()
            Mock Get-HostsFilePath { $script:hostsFile }
            Mock Get-CurrentOS { "Windows" }
        }

        AfterEach {
            Remove-Item $script:hostsFile -ErrorAction SilentlyContinue
        }

        It "removes matching entries" {
            Set-Content $script:hostsFile @(
                "127.0.0.1  localhost"
                "192.168.1.65  local.voxt.ai"
                "192.168.1.65  media.local.voxt.ai"
            )
            Remove-HostEntries -Hostnames "local.voxt.ai","media.local.voxt.ai"
            $content = Get-Content $script:hostsFile
            $content | Should -Contain "127.0.0.1  localhost"
            $content | Should -Not -Contain "192.168.1.65  local.voxt.ai"
            $content | Should -Not -Contain "192.168.1.65  media.local.voxt.ai"
        }

        It "preserves worktree entries" {
            Set-Content $script:hostsFile @(
                "192.168.1.65  local.voxt.ai"
                "192.168.1.65  111222-feature.local.voxt.ai"
                "192.168.1.65  media.111222-feature.local.voxt.ai"
            )
            Remove-HostEntries -Hostnames "local.voxt.ai"
            $content = Get-Content $script:hostsFile
            $content | Should -Not -Contain "192.168.1.65  local.voxt.ai"
            $content | Should -Contain "192.168.1.65  111222-feature.local.voxt.ai"
            $content | Should -Contain "192.168.1.65  media.111222-feature.local.voxt.ai"
        }

        It "does nothing when no entries match" {
            Set-Content $script:hostsFile @(
                "127.0.0.1  localhost"
                "192.168.1.65  other.host"
            )
            Remove-HostEntries -Hostnames "local.voxt.ai"
            @(Get-Content $script:hostsFile) | Should -HaveCount 2
        }
    }

    Context "Get-HostsFilePath" {
        It "returns a non-empty path" {
            $path = Get-HostsFilePath
            $path | Should -Not -BeNullOrEmpty
        }
    }

    Context "Get-CurrentOS" {
        It "returns a known OS string" {
            Get-CurrentOS | Should -BeIn @("Windows", "Docker", "WSL", "Linux", "macOS", "Unknown")
        }
    }
}
