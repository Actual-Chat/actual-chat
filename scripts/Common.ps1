# Common PowerShell utilities for ActualChat scripts

function Get-LocalIP {
    <#
    .SYNOPSIS
        Detects the local LAN IP address (first non-localhost IPv4).
    #>
    $localIp = $null
    if ($IsMacOS) {
        $localIp = (ifconfig | Select-String 'inet (\d+\.\d+\.\d+\.\d+)' -AllMatches).Matches |
            ForEach-Object { $_.Groups[1].Value } |
            Where-Object { $_ -ne '127.0.0.1' } |
            Select-Object -First 1
    } elseif ($IsWindows) {
        $localIp = (Get-NetIPAddress -AddressFamily IPv4 |
            Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.PrefixOrigin -ne 'WellKnown' } |
            Select-Object -First 1).IPAddress
    } else {
        # Linux
        $localIp = (hostname -I 2>$null) -split ' ' | Where-Object { $_ } | Select-Object -First 1
        if (-not $localIp) {
            $localIp = (ip route get 1.1.1.1 2>$null | Select-String 'src (\d+\.\d+\.\d+\.\d+)').Matches.Groups[1].Value
        }
    }
    return $localIp
}

function Set-EnvFileValue {
    <#
    .SYNOPSIS
        Sets a key=value in a .env file. Creates the file if it doesn't exist.
    .PARAMETER Path
        Path to the .env file.
    .PARAMETER Key
        The environment variable name.
    .PARAMETER Value
        The value to set.
    #>
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Key,
        [Parameter(Mandatory)][string]$Value
    )

    $content = if (Test-Path $Path) { Get-Content $Path -Raw } else { "" }
    if (-not $content) { $content = "" }

    if ($content -match "(?m)^$Key=") {
        $content = $content -replace "(?m)^$Key=.*$", "$Key=$Value"
    } else {
        $content = $content.TrimEnd() + "`n$Key=$Value`n"
    }

    Set-Content $Path $content.Trim() -NoNewline
    Add-Content $Path ""  # ensure trailing newline
}

function Update-LocalIP {
    <#
    .SYNOPSIS
        Detects local IP and writes it to .env file.
    .PARAMETER EnvFile
        Path to .env file. Defaults to .env in current directory.
    .OUTPUTS
        The detected local IP address.
    #>
    param(
        [string]$EnvFile = (Join-Path (Get-Location).Path ".env")
    )

    $localIp = Get-LocalIP
    if (-not $localIp) {
        Write-Error "Could not detect local IP address"
        return $null
    }

    Set-EnvFileValue -Path $EnvFile -Key "LOCAL_IP" -Value $localIp
    Write-Host "Updated $EnvFile with LOCAL_IP=$localIp"
    return $localIp
}

function Get-HostsFilePath {
    <#
    .SYNOPSIS
        Returns the platform-specific hosts file path.
    #>
    if ($IsWindows -or $env:OS -eq "Windows_NT") {
        return "$env:SystemRoot\System32\drivers\etc\hosts"
    } else {
        return "/etc/hosts"
    }
}

function Add-HostEntries {
    <#
    .SYNOPSIS
        Adds or updates entries in the hosts file.
    .PARAMETER IP
        The IP address for the hosts entries.
    .PARAMETER Hostnames
        Array of hostnames to add/update.
    .PARAMETER Replace
        If true, replaces existing entries for these hostnames. Otherwise only adds if missing.
    #>
    param(
        [Parameter(Mandatory)][string]$IP,
        [Parameter(Mandatory)][string[]]$Hostnames,
        [switch]$Replace
    )

    $hostsFile = Get-HostsFilePath
    $hostsContent = Get-Content $hostsFile -ErrorAction SilentlyContinue
    if (-not $hostsContent) { $hostsContent = @() }

    $entriesToAdd = @()
    $linesToRemove = @()

    foreach ($hostname in $Hostnames) {
        $escapedHostname = [regex]::Escape($hostname)
        $existingLine = $hostsContent | Where-Object { $_ -match "^\s*[\d\.]+\s+.*$escapedHostname" }
        $newLine = "$IP  $hostname"

        if ($existingLine) {
            if ($Replace -and $existingLine -notmatch "^\s*$([regex]::Escape($IP))\s+") {
                $linesToRemove += $existingLine
                $entriesToAdd += $newLine
            }
        } else {
            $entriesToAdd += $newLine
        }
    }

    if ($entriesToAdd.Count -eq 0 -and $linesToRemove.Count -eq 0) {
        Write-Host "Hosts entries already up-to-date"
        return
    }

    # Build the PowerShell command to run with elevation
    $script = ""
    if ($linesToRemove.Count -gt 0) {
        $patterns = ($Hostnames | ForEach-Object { [regex]::Escape($_) }) -join '|'
        $script += "`$content = Get-Content '$hostsFile' | Where-Object { `$_ -notmatch '^\s*[\d\.]+\s+.*($patterns)' }; "
        $script += "Set-Content '$hostsFile' `$content -Force; "
    }
    if ($entriesToAdd.Count -gt 0) {
        $newEntries = $entriesToAdd -join "`n"
        $script += "Add-Content '$hostsFile' `"``n$newEntries`""
    }

    if ($IsWindows -or $env:OS -eq "Windows_NT") {
        try {
            Invoke-Expression $script
            Write-Host "Updated hosts file"
        } catch {
            Write-Host "Requesting elevation to update hosts file..."
            try {
                Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -Command $script" -Wait -ErrorAction Stop
                Write-Host "Updated hosts file (via UAC)"
            } catch {
                Write-Host "Could not update hosts file. Add manually:" -ForegroundColor Yellow
                $entriesToAdd | ForEach-Object { Write-Host $_ }
            }
        }
    } else {
        Write-Host "Updating hosts file (sudo required)..."
        $sudoScript = $script -replace "'", "'\\''"
        bash -c "sudo pwsh -NoProfile -c '$sudoScript'"
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Updated hosts file"
        } else {
            Write-Host "Could not update hosts file. Add manually:" -ForegroundColor Yellow
            $entriesToAdd | ForEach-Object { Write-Host $_ }
        }
    }
}

function Update-HostEntries {
    <#
    .SYNOPSIS
        Detects local IP and adds/updates hosts file entries.
    .PARAMETER Hostnames
        Array of hostnames to add/update.
    .OUTPUTS
        The detected local IP address.
    #>
    param(
        [Parameter(Mandatory)][string[]]$Hostnames
    )

    $localIp = Get-LocalIP
    if (-not $localIp) {
        Write-Error "Could not detect local IP address"
        return $null
    }

    Add-HostEntries -IP $localIp -Hostnames $Hostnames -Replace
    return $localIp
}

function Remove-HostEntries {
    <#
    .SYNOPSIS
        Removes entries from the hosts file by hostname.
    .PARAMETER Hostnames
        Array of hostnames to remove.
    #>
    param(
        [Parameter(Mandatory)][string[]]$Hostnames
    )

    $hostsFile = Get-HostsFilePath
    $hostsContent = Get-Content $hostsFile -ErrorAction SilentlyContinue
    if (-not $hostsContent) { return }

    $newContent = $hostsContent | Where-Object {
        $line = $_
        $shouldKeep = $true
        foreach ($hostname in $Hostnames) {
            if ($line -match [regex]::Escape($hostname)) {
                $shouldKeep = $false
                break
            }
        }
        $shouldKeep
    }

    if ($newContent.Count -eq $hostsContent.Count) {
        Write-Host "No matching hosts entries found"
        return
    }

    if ($IsWindows -or $env:OS -eq "Windows_NT") {
        try {
            Set-Content -Path $hostsFile -Value $newContent -ErrorAction Stop
            Write-Host "Removed hosts entries"
        } catch {
            Write-Host "Requesting elevation to update hosts file..."
            try {
                $tempFile = [System.IO.Path]::GetTempFileName()
                $newContent | Set-Content -Path $tempFile
                $script = "Copy-Item '$tempFile' '$hostsFile' -Force; Remove-Item '$tempFile'"
                Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -Command $script" -Wait -ErrorAction Stop
                Write-Host "Removed hosts entries (via UAC)"
            } catch {
                Write-Host "Could not update hosts file" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "Removing hosts entries (sudo required)..."
        $tempFile = [System.IO.Path]::GetTempFileName()
        $newContent | Set-Content -Path $tempFile
        bash -c "sudo cp '$tempFile' '$hostsFile' && rm '$tempFile'"
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Removed hosts entries"
        } else {
            Write-Host "Could not update hosts file" -ForegroundColor Yellow
            Remove-Item $tempFile -ErrorAction SilentlyContinue
        }
    }
}

function Get-ScriptDirectory {
    <#
    .SYNOPSIS
        Gets the directory containing the calling script.
    #>
    $dir = $PSScriptRoot
    if (-not $dir -and $PSCommandPath) { $dir = Split-Path -Parent $PSCommandPath }
    if (-not $dir) { $dir = (Get-Location).Path }
    return $dir
}
