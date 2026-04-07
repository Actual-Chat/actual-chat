#!/usr/bin/env pwsh
# Functions to configure the hosts file, the LOCAL_IP entry in .env, and trust
# dev SSL certificates. Idempotent: skips work that's already done. Only
# requests admin (Windows) or sudo (macOS/Linux) when an action actually needs
# it.
#
# This file contains only function definitions. Depends on scripts/Common.ps1
# being dot-sourced into the same scope. To use it:
#
#     pwsh -NoProfile -c ". ./scripts/Common.ps1; . ./add-hosts.ps1; Configure-LocalEnvHosts"

function Get-CertThumbprint {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($Path)
    return $cert.Thumbprint
}

function Test-RootCertInstalled {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    switch (Get-CurrentOS) {
        'Windows' {
            $thumbprint = Get-CertThumbprint -Path $Path
            $store = [System.Security.Cryptography.X509Certificates.X509Store]::new('Root', 'LocalMachine')
            try {
                $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
                return [bool]($store.Certificates | Where-Object { $_.Thumbprint -ieq $thumbprint })
            } finally {
                $store.Close()
            }
        }
        'macOS' {
            $thumbprint = Get-CertThumbprint -Path $Path
            $found = security find-certificate -a -Z /Library/Keychains/System.keychain 2>$null |
                Select-String -Pattern "SHA-1 hash: $thumbprint" -SimpleMatch -Quiet
            return [bool]$found
        }
        default {
            # Linux / WSL / Docker
            $destPath = '/usr/local/share/ca-certificates/rootCA.crt'
            if (-not (Test-Path $destPath)) { return $false }
            return (Get-FileHash $Path -Algorithm SHA256).Hash -eq (Get-FileHash $destPath -Algorithm SHA256).Hash
        }
    }
}

function Install-RootCert {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    switch (Get-CurrentOS) {
        'Windows' {
            Write-Host "Trusting root certificate (admin required)..."
            try {
                $proc = Start-Process certutil `
                    -ArgumentList @('-addstore', '-f', 'ROOT', "`"$Path`"") `
                    -Verb RunAs -Wait -PassThru -ErrorAction Stop
                if ($proc.ExitCode -ne 0) {
                    Write-Host "certutil exited with code $($proc.ExitCode)" -ForegroundColor Yellow
                }
            } catch {
                Write-Host "Could not elevate to install root certificate." -ForegroundColor Yellow
                Write-Host "Run manually as admin:" -ForegroundColor Yellow
                Write-Host "  certutil -addstore -f ROOT `"$Path`""
            }
        }
        'macOS' {
            Write-Host "Trusting root certificate (sudo required)..."
            sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain $Path
        }
        default {
            Write-Host "Trusting root certificate (sudo required)..."
            sudo cp $Path /usr/local/share/ca-certificates/rootCA.crt
            sudo update-ca-certificates
        }
    }
}

function Test-DotnetDevCertTrusted {
    [CmdletBinding()]
    param()
    # `--check --trust` returns 0 only if a trusted dev cert exists.
    # On Linux, --trust is unsupported, so fall back to existence check.
    if ((Get-CurrentOS) -in @('Windows', 'macOS')) {
        & dotnet dev-certs https --check --trust *> $null
    } else {
        & dotnet dev-certs https --check *> $null
    }
    return $LASTEXITCODE -eq 0
}

function Install-DotnetDevCert {
    [CmdletBinding()]
    param()
    if ((Get-CurrentOS) -eq 'Windows') {
        $pfxPath = Join-Path $env:USERPROFILE '.aspnet/https/aspnetapp.pfx'
        $pfxDir = Split-Path -Parent $pfxPath
        if (-not (Test-Path $pfxDir)) {
            New-Item -ItemType Directory -Path $pfxDir -Force | Out-Null
        }
        if (-not (Test-Path $pfxPath)) {
            Write-Host "Exporting dev cert PFX to $pfxPath..."
            & dotnet dev-certs https -ep $pfxPath -p crypticpassword
        }
    }
    Write-Host "Installing dotnet dev-certs (--trust)..."
    & dotnet dev-certs https --trust
}

function Configure-LocalEnvHosts {
    [CmdletBinding()]
    param(
        [switch]$Force
    )

    # 1. Hosts file + .env LOCAL_IP. Update-HostEntries already elevates only
    #    when the file actually needs changes.
    Write-Host "Updating hosts file and .env LOCAL_IP..."
    Update-HostEntries -DetectIP -Hostnames @('local.voxt.ai', 'media.local.voxt.ai', 'cdn.local.voxt.ai') | Out-Null
    Update-HostEntries -DetectIP -Hostnames @('local.actual.chat', 'media.local.actual.chat', 'cdn.local.actual.chat') | Out-Null
    Update-LocalIP | Out-Null

    # 2. Root CA
    $certPath = Join-Path $PSScriptRoot '.config/local.voxt.ai/ssl/rootCA.crt'
    if (-not (Test-Path $certPath)) {
        Write-Error "Root CA certificate not found: $certPath"
        return
    }

    if (-not $Force -and (Test-RootCertInstalled -Path $certPath)) {
        Write-Host "Root certificate already trusted - skipping"
    } else {
        Install-RootCert -Path $certPath
    }

    # 3. .NET dev cert
    if (-not $Force -and (Test-DotnetDevCertTrusted)) {
        Write-Host "dotnet dev-certs already trusted - skipping"
    } else {
        Install-DotnetDevCert
    }

    Write-Host "Done."
}
