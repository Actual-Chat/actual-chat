BeforeDiscovery {
    # The "MacOSRootCertStore.IsInstalled" Context uses Get-CurrentOS in a
    # -Skip expression, which Pester evaluates during the discovery phase -
    # before BeforeAll runs - so Common.ps1 must be loaded into the discovery
    # scope as well.
    . "$PSScriptRoot/../../../scripts/Common.ps1"
    . "$PSScriptRoot/../../../add-hosts.ps1"
}

Describe "add-hosts.ps1 (e2e)" {
    BeforeAll {
        . "$PSScriptRoot/../../../scripts/Common.ps1"
        . "$PSScriptRoot/../../../add-hosts.ps1"
        $script:certPath = (Resolve-Path "$PSScriptRoot/../../../.config/local.voxt.ai/ssl/rootCA.crt").Path
        $script:os = Get-CurrentOS
    }

    Context "MacOSRootCertStore.IsInstalled (real keychain)" -Skip:((Get-CurrentOS) -ne 'macOS') {
        It "returns a Boolean for the bundled rootCA.crt" {
            # Real call against the System keychain - we don't assert true/false
            # because the result depends on the developer's local trust state.
            [MacOSRootCertStore]::new().IsInstalled($script:certPath) | Should -BeOfType [bool]
        }
    }

    Context "DotnetDevCert.IsTrusted (real dotnet)" {
        It "returns a Boolean" {
            # Real call to `dotnet dev-certs https --check` - read-only.
            [DotnetDevCert]::new().IsTrusted() | Should -BeOfType [bool]
        }
    }

    Context "Configure-LocalEnvHosts" {
        # Cross-platform e2e tests for the orchestrator. We do NOT pin
        # Get-CurrentOS - the real factory dispatches to whichever subclass
        # matches the host OS, and assertions branch on $script:os. The only
        # mocks are at the OS-side-effect boundary so the host stays clean:
        # `sudo` (mac/linux), `Start-Process` (windows certutil), `dotnet`
        # (would mutate the dev cert), `Update-HostEntries` / `Update-LocalIP`
        # (would mutate /etc/hosts and .env).
        BeforeEach {
            Mock Update-HostEntries { }
            Mock Update-LocalIP { }
            Mock dotnet { $global:LASTEXITCODE = 0 }
            if ($script:os -eq 'Windows') {
                Mock Start-Process { [pscustomobject]@{ ExitCode = 0 } }
            } else {
                # `sudo` does not exist as a command on Windows, so we only
                # register the mock on platforms where the relevant code path
                # would actually invoke it.
                Mock sudo { }
            }
        }

        It "updates hosts entries twice and LOCAL_IP once" {
            Configure-LocalEnvHosts
            Should -Invoke Update-HostEntries -Times 2 -Exactly
            Should -Invoke Update-LocalIP -Times 1 -Exactly
        }

        It "calls voxt.ai and actual.chat host sets" {
            Configure-LocalEnvHosts
            Should -Invoke Update-HostEntries -ParameterFilter { $Hostnames -contains 'local.voxt.ai' }
            Should -Invoke Update-HostEntries -ParameterFilter { $Hostnames -contains 'local.actual.chat' }
        }

        It "with -Force, runs root cert install via the OS-appropriate command" {
            Configure-LocalEnvHosts -Force
            switch ($script:os) {
                'Windows' {
                    Should -Invoke Start-Process -ParameterFilter { $FilePath -eq 'certutil' }
                }
                'macOS' {
                    Should -Invoke sudo -ParameterFilter { ($args -join ' ') -match 'security add-trusted-cert' }
                }
                default {
                    # Linux / WSL / Docker
                    Should -Invoke sudo -ParameterFilter { ($args -join ' ') -match '^cp ' }
                    Should -Invoke sudo -ParameterFilter { ($args -join ' ') -eq 'update-ca-certificates' }
                }
            }
        }

        It "with -Force, runs dotnet dev-certs https --trust" {
            Configure-LocalEnvHosts -Force
            Should -Invoke dotnet -ParameterFilter { ($args -join ' ') -eq 'dev-certs https --trust' }
        }

        It "calls dotnet dev-certs --check when not forced" {
            Configure-LocalEnvHosts
            Should -Invoke dotnet -ParameterFilter { ($args -join ' ') -match '^dev-certs https --check' }
        }

        It "when dotnet --check fails, runs dotnet --trust" {
            Mock dotnet {
                if (($args -join ' ') -match '--check') { $global:LASTEXITCODE = 1 }
                else { $global:LASTEXITCODE = 0 }
            }
            Configure-LocalEnvHosts
            Should -Invoke dotnet -ParameterFilter { ($args -join ' ') -eq 'dev-certs https --trust' }
        }

        It "when cert file is missing, writes error and skips installs" {
            Mock Test-Path { $false } -ParameterFilter { $Path -match 'rootCA\.crt$' }
            Configure-LocalEnvHosts -ErrorAction SilentlyContinue
            Should -Invoke dotnet -Times 0
            if ($script:os -eq 'Windows') {
                Should -Invoke Start-Process -Times 0
            } else {
                Should -Invoke sudo -Times 0
            }
        }
    }
}
