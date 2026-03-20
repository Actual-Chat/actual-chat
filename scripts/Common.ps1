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
