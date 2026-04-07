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
        # matches the host OS, and assertions branch on $script:os.
        #
        # Update-HostEntries AND Update-LocalIP both run for real:
        #   - Get-HostsFilePath is mocked to return a temp file, so the real
        #     Update-HostEntries writes its entries to a user-writable file
        #     (no sudo / UAC). Common.ps1's direct-write-then-elevate logic
        #     means the temp-file write succeeds without elevation.
        #   - Push-Location to a temp dir redirects Update-LocalIP's default
        #     `./.env` target to the same temp dir.
        #
        # Mocks are kept to the minimum needed to keep the host clean:
        #   - Get-HostsFilePath: redirected to a temp hosts file (above).
        #   - Get-LocalIP: stubbed to a deterministic LAN IP so the .env
        #     assertion is independent of the host's network config.
        #   - dotnet ... --trust: mutates the user's dev cert. Only the
        #     --trust invocation is intercepted; --check runs for real.
        #   - sudo (Mac/Linux): would actually elevate.
        #   - Start-Process (Windows): would prompt UAC for certutil.
        BeforeEach {
            $script:tempDir = New-Item -ItemType Directory -Path ([System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "addhosts-e2e-$(Get-Random)"))
            $script:tempHostsFile = Join-Path $script:tempDir 'hosts'
            Set-Content -Path $script:tempHostsFile -Value '' -NoNewline
            Push-Location $script:tempDir

            Mock Get-HostsFilePath { $script:tempHostsFile }
            Mock Get-LocalIP { '192.168.42.42' }
            Mock dotnet { $global:LASTEXITCODE = 0 } -ParameterFilter { ($args -join ' ') -match '--trust' }
            if ($script:os -eq 'Windows') {
                Mock Start-Process { [pscustomobject]@{ ExitCode = 0 } }
            } else {
                # `sudo` does not exist as a command on Windows, so we only
                # register the mock on platforms where the relevant code path
                # would actually invoke it.
                Mock sudo { }
            }
        }

        AfterEach {
            Pop-Location -ErrorAction SilentlyContinue
            if ($script:tempDir) {
                Remove-Item $script:tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It "writes LOCAL_IP to .env in cwd via real Update-LocalIP" {
            Configure-LocalEnvHosts
            $envFile = Join-Path $script:tempDir '.env'
            $envFile | Should -Exist
            Get-Content $envFile -Raw | Should -Match 'LOCAL_IP=192\.168\.42\.42'
        }

        It "writes voxt.ai and actual.chat host entries to the hosts file via real Update-HostEntries" {
            Configure-LocalEnvHosts
            $hostsContent = Get-Content $script:tempHostsFile -Raw
            $hostsContent | Should -Match '192\.168\.42\.42\s+local\.voxt\.ai'
            $hostsContent | Should -Match '192\.168\.42\.42\s+media\.local\.voxt\.ai'
            $hostsContent | Should -Match '192\.168\.42\.42\s+cdn\.local\.voxt\.ai'
            $hostsContent | Should -Match '192\.168\.42\.42\s+local\.actual\.chat'
            $hostsContent | Should -Match '192\.168\.42\.42\s+media\.local\.actual\.chat'
            $hostsContent | Should -Match '192\.168\.42\.42\s+cdn\.local\.actual\.chat'
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

        It "when dotnet --check fails, runs dotnet --trust" {
            # Override the unfiltered --check fall-through with a non-zero exit.
            Mock dotnet { $global:LASTEXITCODE = 1 } -ParameterFilter { ($args -join ' ') -match '--check' }
            Configure-LocalEnvHosts
            Should -Invoke dotnet -ParameterFilter { ($args -join ' ') -eq 'dev-certs https --trust' }
        }

        It "when cert file is missing, writes error and skips installs" {
            Mock Test-Path { $false } -ParameterFilter { $Path -match 'rootCA\.crt$' }
            Configure-LocalEnvHosts -ErrorAction SilentlyContinue
            Should -Invoke dotnet -Times 0 -ParameterFilter { ($args -join ' ') -match '--trust' }
            if ($script:os -eq 'Windows') {
                Should -Invoke Start-Process -Times 0
            } else {
                Should -Invoke sudo -Times 0
            }
        }
    }
}
