Describe "Common.ps1 (e2e)" {
    BeforeAll {
        . "$PSScriptRoot/../../../scripts/Common.ps1"
    }

    Context "Get-LocalIP (real network detection)" {
        # Real `ifconfig` / `Get-NetIPAddress` / `ip -4 -o addr show` runs
        # under the hood. The unit tests in unit/Common.Tests.ps1 cover
        # Select-LanIPv4 with synthetic input; these tests cover the actual
        # OS network enumeration that the unit tests bypass. A broken parser
        # surfaces here as null or invalid output from Get-LocalIP.
        BeforeAll {
            $script:localIp = Get-LocalIP
        }

        It "returns a non-null result (host has a routable LAN interface)" {
            # CI runners (windows-latest, macos-latest) and any normal dev box
            # always have at least one non-loopback, non-link-local IPv4. If
            # this fails on a real machine, either the host has only loopback
            # or NonLanInterfaceRegex is over-filtering.
            $script:localIp | Should -Not -BeNullOrEmpty
        }

        It "returns a parseable IPv4" {
            { [ipaddress]::Parse($script:localIp) } | Should -Not -Throw
            $script:localIp | Should -Match '^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$'
        }

        It "is not loopback (127/8)" {
            $script:localIp | Should -Not -Match '^127\.'
        }

        It "is not link-local APIPA (169.254/16)" {
            $script:localIp | Should -Not -Match '^169\.254\.'
        }
    }
}
