BeforeDiscovery {
    # The "LinuxRootCertStore.IsInstalled" Context uses [LinuxRootCertStore]
    # in a -Skip expression, which Pester evaluates during the discovery
    # phase - before BeforeAll runs - so the type must be loaded into the
    # discovery scope as well.
    . "$PSScriptRoot/../../../scripts/Common.ps1"
    . "$PSScriptRoot/../../../add-hosts.ps1"
}

Describe "add-hosts.ps1 (unit)" {
    BeforeAll {
        . "$PSScriptRoot/../../../scripts/Common.ps1"
        . "$PSScriptRoot/../../../add-hosts.ps1"
        $script:certPath = (Resolve-Path "$PSScriptRoot/../../../.config/local.voxt.ai/ssl/rootCA.crt").Path
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

    Context "LinuxRootCertStore.IsInstalled" {
        BeforeAll {
            $script:linuxDest = [LinuxRootCertStore]::DestPath
        }

        It "uses a namespaced filename (not generic rootCA.crt)" {
            # Guards against accidental clobber of certs from mkcert / other tools
            [LinuxRootCertStore]::DestPath | Should -Not -Be '/usr/local/share/ca-certificates/rootCA.crt'
            [LinuxRootCertStore]::DestPath | Should -Match '^/usr/local/share/ca-certificates/voxt[-.].+\.crt$'
        }

        It "returns false when destination is missing" -Skip:(Test-Path ([LinuxRootCertStore]::DestPath)) {
            [LinuxRootCertStore]::new().IsInstalled($script:certPath) | Should -BeFalse
        }

        It "returns true when destination exists with the same hash" {
            Mock Test-Path { $true } -ParameterFilter { $Path -eq $script:linuxDest }
            Mock Get-FileHash {
                [pscustomobject]@{ Hash = 'AAAA' }
            } -ParameterFilter { $Path -eq $script:linuxDest }
            Mock Get-FileHash {
                [pscustomobject]@{ Hash = 'AAAA' }
            } -ParameterFilter { $Path -eq $script:certPath }
            [LinuxRootCertStore]::new().IsInstalled($script:certPath) | Should -BeTrue
        }

        It "returns false when destination exists with a different hash" {
            Mock Test-Path { $true } -ParameterFilter { $Path -eq $script:linuxDest }
            Mock Get-FileHash {
                [pscustomobject]@{ Hash = 'AAAA' }
            } -ParameterFilter { $Path -eq $script:linuxDest }
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
    }

    Context "WindowsRootCertStore.Install" {
        It "calls Start-Process certutil with the cert path and -Verb RunAs" {
            Mock Start-Process { [pscustomobject]@{ ExitCode = 0 } }
            [WindowsRootCertStore]::new().Install('C:\fake\rootCA.crt')
            Should -Invoke Start-Process -Times 1 -Exactly -ParameterFilter {
                $FilePath -eq 'certutil' -and
                $Verb -eq 'RunAs' -and
                ($ArgumentList -join ' ') -match '-addstore -f ROOT' -and
                ($ArgumentList -join ' ') -match 'rootCA\.crt'
            }
        }

        It "does not throw when certutil exits non-zero (prints warning)" {
            Mock Start-Process { [pscustomobject]@{ ExitCode = 5 } }
            { [WindowsRootCertStore]::new().Install('C:\fake\rootCA.crt') } | Should -Not -Throw
        }

        It "does not throw when Start-Process itself throws (prints manual instructions)" {
            Mock Start-Process { throw "elevation cancelled" }
            { [WindowsRootCertStore]::new().Install('C:\fake\rootCA.crt') } | Should -Not -Throw
        }
    }

    Context "MacOSRootCertStore.Install" -Skip:((Get-CurrentOS) -ne 'macOS') {
        # `sudo` doesn't exist on Windows, so Pester can't mock it there.
        It "shells out to sudo security add-trusted-cert with the cert path" {
            Mock sudo { }
            [MacOSRootCertStore]::new().Install('/tmp/fake-rootCA.crt')
            Should -Invoke sudo -Times 1 -Exactly -ParameterFilter {
                ($args -join ' ') -match 'security add-trusted-cert' -and
                ($args -join ' ') -match '/Library/Keychains/System\.keychain' -and
                ($args -join ' ') -match '/tmp/fake-rootCA\.crt'
            }
        }
    }

    Context "LinuxRootCertStore.Install" -Skip:((Get-CurrentOS) -notin @('Linux', 'Docker', 'WSL')) {
        # `sudo` doesn't exist on Windows, so Pester can't mock it there.
        It "calls sudo cp then sudo update-ca-certificates" {
            Mock sudo { }
            [LinuxRootCertStore]::new().Install('/tmp/fake-rootCA.crt')
            Should -Invoke sudo -Times 2 -Exactly
            Should -Invoke sudo -ParameterFilter {
                ($args -join ' ') -match '^cp /tmp/fake-rootCA\.crt /usr/local/share/ca-certificates/voxt'
            }
            Should -Invoke sudo -ParameterFilter {
                ($args -join ' ') -eq 'update-ca-certificates'
            }
        }

        It "uses the namespaced DestPath as the copy target" {
            Mock sudo { }
            [LinuxRootCertStore]::new().Install('/tmp/fake-rootCA.crt')
            Should -Invoke sudo -ParameterFilter {
                ($args -join ' ').EndsWith([LinuxRootCertStore]::DestPath)
            }
        }
    }

    Context "DotnetDevCert.Install" {
        BeforeEach {
            # USERPROFILE is unset on macOS/Linux but DotnetDevCert.Install uses
            # it as the PFX export base on the Windows branch.
            $script:savedUserProfile = $env:USERPROFILE
            $env:USERPROFILE = [System.IO.Path]::GetTempPath().TrimEnd([System.IO.Path]::DirectorySeparatorChar)
        }
        AfterEach {
            if ($null -ne $script:savedUserProfile) {
                $env:USERPROFILE = $script:savedUserProfile
            } else {
                Remove-Item Env:USERPROFILE -ErrorAction SilentlyContinue
            }
        }

        It "on non-Windows: only invokes 'dotnet dev-certs https --trust'" {
            Mock Get-CurrentOS { 'macOS' }
            Mock dotnet { $global:LASTEXITCODE = 0 }
            [DotnetDevCert]::new().Install()
            Should -Invoke dotnet -Times 1 -Exactly -ParameterFilter {
                ($args -join ' ') -eq 'dev-certs https --trust'
            }
        }

        It "on Windows when PFX missing: creates dir, exports PFX, then trusts" {
            Mock Get-CurrentOS { 'Windows' }
            Mock Test-Path { $false }
            Mock New-Item { }
            Mock dotnet { $global:LASTEXITCODE = 0 }
            [DotnetDevCert]::new().Install()
            Should -Invoke New-Item -Times 1 -ParameterFilter { $ItemType -eq 'Directory' }
            Should -Invoke dotnet -Times 2 -Exactly
            Should -Invoke dotnet -ParameterFilter { ($args -join ' ') -match 'dev-certs https -ep .* -p crypticpassword' }
            Should -Invoke dotnet -ParameterFilter { ($args -join ' ') -eq 'dev-certs https --trust' }
        }

        It "on Windows when PFX exists: skips export, only trusts" {
            Mock Get-CurrentOS { 'Windows' }
            Mock Test-Path { $true }
            Mock dotnet { $global:LASTEXITCODE = 0 }
            [DotnetDevCert]::new().Install()
            Should -Invoke dotnet -Times 1 -Exactly -ParameterFilter {
                ($args -join ' ') -eq 'dev-certs https --trust'
            }
        }
    }

    Context "Configure-LocalEnvHosts function shape" {
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
