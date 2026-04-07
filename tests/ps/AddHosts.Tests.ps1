BeforeDiscovery {
    # Skip conditions on individual Contexts call Get-CurrentOS, which Pester
    # evaluates during the discovery phase - before BeforeAll runs - so we
    # need Common.ps1 dot-sourced into the discovery scope.
    . "$PSScriptRoot/../../scripts/Common.ps1"
}

Describe "add-hosts.ps1" {
    BeforeAll {
        . "$PSScriptRoot/../../scripts/Common.ps1"
        . "$PSScriptRoot/../../add-hosts.ps1"
        $script:certPath = (Resolve-Path "$PSScriptRoot/../../.config/local.voxt.ai/ssl/rootCA.crt").Path
        $script:expectedThumbprint = ([System.Security.Cryptography.X509Certificates.X509Certificate2]::new($script:certPath)).Thumbprint
    }

    Context "RootCertStore.GetThumbprint" {
        It "returns the SHA-1 thumbprint of the bundled rootCA.crt" {
            [RootCertStore]::GetThumbprint($script:certPath) | Should -Be $script:expectedThumbprint
        }

        It "is a 40-char uppercase hex string" {
            [RootCertStore]::GetThumbprint($script:certPath) | Should -Match '^[0-9A-F]{40}$'
        }

        It "throws when the file does not exist" {
            { [RootCertStore]::GetThumbprint('/nonexistent/cert.crt') } | Should -Throw
        }
    }

    Context "RootCertStore abstract base" {
        It "IsInstalled throws on the base class" {
            $base = [RootCertStore]::new()
            { $base.IsInstalled('x') } | Should -Throw
        }

        It "Install throws on the base class" {
            $base = [RootCertStore]::new()
            { $base.Install('x') } | Should -Throw
        }
    }

    Context "RootCertStore.ForCurrentOS factory" {
        It "returns WindowsRootCertStore on Windows" {
            Mock Get-CurrentOS { 'Windows' }
            [RootCertStore]::ForCurrentOS().GetType().Name | Should -Be 'WindowsRootCertStore'
        }

        It "returns MacOSRootCertStore on macOS" {
            Mock Get-CurrentOS { 'macOS' }
            [RootCertStore]::ForCurrentOS().GetType().Name | Should -Be 'MacOSRootCertStore'
        }

        It "returns LinuxRootCertStore on Linux" {
            Mock Get-CurrentOS { 'Linux' }
            [RootCertStore]::ForCurrentOS().GetType().Name | Should -Be 'LinuxRootCertStore'
        }

        It "returns LinuxRootCertStore on Docker" {
            Mock Get-CurrentOS { 'Docker' }
            [RootCertStore]::ForCurrentOS().GetType().Name | Should -Be 'LinuxRootCertStore'
        }

        It "returns LinuxRootCertStore on WSL" {
            Mock Get-CurrentOS { 'WSL' }
            [RootCertStore]::ForCurrentOS().GetType().Name | Should -Be 'LinuxRootCertStore'
        }

        It "returns LinuxRootCertStore on Unknown OS (default branch)" {
            Mock Get-CurrentOS { 'Unknown' }
            [RootCertStore]::ForCurrentOS().GetType().Name | Should -Be 'LinuxRootCertStore'
        }
    }

    Context "Subclasses inherit from RootCertStore" {
        It "WindowsRootCertStore is a RootCertStore" {
            [WindowsRootCertStore]::new() -is [RootCertStore] | Should -BeTrue
        }

        It "MacOSRootCertStore is a RootCertStore" {
            [MacOSRootCertStore]::new() -is [RootCertStore] | Should -BeTrue
        }

        It "LinuxRootCertStore is a RootCertStore" {
            [LinuxRootCertStore]::new() -is [RootCertStore] | Should -BeTrue
        }
    }

    Context "MacOSRootCertStore.IsInstalled" -Skip:((Get-CurrentOS) -ne 'macOS') {
        It "returns a Boolean for the bundled rootCA.crt" {
            # Real call against the System keychain - we don't assert true/false
            # because the result depends on the developer's local trust state.
            [MacOSRootCertStore]::new().IsInstalled($script:certPath) | Should -BeOfType [bool]
        }
    }

    Context "LinuxRootCertStore.IsInstalled" {
        It "returns false when /usr/local/share/ca-certificates/rootCA.crt is missing" -Skip:(Test-Path '/usr/local/share/ca-certificates/rootCA.crt') {
            [LinuxRootCertStore]::new().IsInstalled($script:certPath) | Should -BeFalse
        }

        It "returns true when destination exists with the same hash" {
            $tempCert = [System.IO.Path]::GetTempFileName()
            try {
                Copy-Item $script:certPath $tempCert -Force
                Mock Test-Path { $true } -ParameterFilter { $Path -eq '/usr/local/share/ca-certificates/rootCA.crt' }
                Mock Get-FileHash {
                    [pscustomobject]@{ Hash = 'AAAA' }
                } -ParameterFilter { $Path -eq '/usr/local/share/ca-certificates/rootCA.crt' }
                Mock Get-FileHash {
                    [pscustomobject]@{ Hash = 'AAAA' }
                } -ParameterFilter { $Path -eq $script:certPath }
                [LinuxRootCertStore]::new().IsInstalled($script:certPath) | Should -BeTrue
            } finally {
                Remove-Item $tempCert -ErrorAction SilentlyContinue
            }
        }

        It "returns false when destination exists with a different hash" {
            Mock Test-Path { $true } -ParameterFilter { $Path -eq '/usr/local/share/ca-certificates/rootCA.crt' }
            Mock Get-FileHash {
                [pscustomobject]@{ Hash = 'AAAA' }
            } -ParameterFilter { $Path -eq '/usr/local/share/ca-certificates/rootCA.crt' }
            Mock Get-FileHash {
                [pscustomobject]@{ Hash = 'BBBB' }
            } -ParameterFilter { $Path -eq $script:certPath }
            [LinuxRootCertStore]::new().IsInstalled($script:certPath) | Should -BeFalse
        }
    }

    Context "DotnetDevCert" {
        It "is constructible" {
            [DotnetDevCert]::new() | Should -Not -BeNullOrEmpty
        }

        It "IsTrusted returns a Boolean" {
            # Real call to `dotnet dev-certs https --check` - read-only.
            [DotnetDevCert]::new().IsTrusted() | Should -BeOfType [bool]
        }
    }

    Context "Configure-LocalEnvHosts function" {
        It "is defined" {
            Get-Command Configure-LocalEnvHosts -CommandType Function -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
        }

        It "has a -Force switch parameter" {
            $cmd = Get-Command Configure-LocalEnvHosts
            $cmd.Parameters.ContainsKey('Force') | Should -BeTrue
            $cmd.Parameters['Force'].SwitchParameter | Should -BeTrue
        }
    }
}
