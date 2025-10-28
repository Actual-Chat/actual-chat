:<<BATCH
    @echo off

    echo detecting permissions...
    net session >nul 2>&1
    if %errorLevel% == 0 (
        echo Success: Administrative permissions confirmed.
    ) else (
        echo Failure: Administrative permissions required
        exit /b 1
    )

    echo patching hosts file...
    set hostsFile=%WINDIR%\system32\drivers\etc\hosts
    echo determining ip address...
    FOR /F "tokens=4 delims= " %%i in ('route print ^| find " 0.0.0.0" ^| find " 192.168."') do set localIp=%%i
    if "%localIp%"=="" (
        FOR /F "tokens=4 delims= " %%i in ('route print ^| find " 0.0.0.0"') do set localIp=%%i
    )
    echo "local ip: %localIp%"
    set hosts=local.voxt.ai media.local.voxt.ai cdn.local.voxt.ai
    set altHosts=local.actual.chat media.local.actual.chat cdn.local.actual.chat
    set hostsLine=%localIp%  %hosts%
    set altHostsLine=%localIp%  %altHosts%

    set "removeHostEntriesScript=@(Get-Content '%hostsFile%' | where-object { $_ -notmatch '[0-9\.]+\s+%hosts%.*' } | where-object { $_ -notmatch '[0-9\.]+\s+%altHosts%.*' }) + '' | Set-Content '%hostsFile%' -Force"
    powershell -Command "%removeHostEntriesScript%";

    set addHostEntryScript="Add-Content -Path '%hostsFile%' -Value '%hostsLine%'"
    set addAltHostEntryScript="Add-Content -Path '%hostsFile%' -Value '%altHostsLine%'"
    powershell -Command %addHostEntryScript%;
    powershell -Command %addAltHostEntryScript%;
    echo hosts file patched

    set wd=%~dp0
    set certFilePath=%wd%.config\local.voxt.ai\ssl\local.voxt.ai.crt
    echo trusting certificate '%certFilePath%'...
    certutil -addstore -f "ROOT" "%certFilePath%"

    echo installing dotnet dev certs
    dotnet dev-certs https -ep $env:USERPROFILE\.aspnet\https\aspnetapp.pfx -p crypticpassword
    dotnet dev-certs https --trust

    pause
    exit /b
BATCH

#!/bin/sh

updateHostsFile() {
  IP=$1
  HOST=$2
  FILE=$3

  sudo touch $FILE
  line="$IP  $HOST"

  if sudo grep -qF -- "$line" "$FILE"; then
    echo "hosts is up-to-date, skipped"
    return 1;
  fi

  if sudo grep -qF -- "$HOST" "$FILE"; then
    sudo sed -i.bak "/$HOST/ s/.*/$line/g" "$FILE";
  else
    echo "$line" | sudo tee -a "$FILE";
  fi
}

trustCertificate() {
    certPath=.config/local.voxt.ai/ssl/local.voxt.ai.crt
    case `uname` in
      Darwin)
        sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain $certPath
      ;;
      Linux)
        # commands for Linux go here
        sudo cp $certPath /usr/local/share/ca-certificates/
        sudo update-ca-certificates
      ;;
      *)
        # commands for FreeBSD go here
        echo "Not supported OS!" 1>&2
        exit 1
      ;;
    esac
}

echo patching hosts...
localIp=$(ifconfig | grep "inet " | grep -Fv 127.0.0.1 | awk '{print $2}' | head -1)
[ -z "$localIp" ] && echo "Failed to detect local ip address" && exit 1
updateHostsFile "$localIp" "local.voxt.ai media.local.voxt.ai cdn.local.voxt.ai" "/etc/hosts"
updateHostsFile "$localIp" "local.actual.chat media.local.actual.chat cdn.local.actual.chat" "/etc/hosts"

echo trusting voxt.ai certificate...
trustCertificate

echo installing dotnet dev certs...
dotnet dev-certs https --trust
