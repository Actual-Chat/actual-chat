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
    FOR /F "tokens=4 delims= " %%i in ('route print ^| find " 0.0.0.0"') do set localIp=%%i
    set hosts=local.actual.chat media.local.actual.chat cdn.local.actual.chat
    set hostsLine=%localIp%  local.actual.chat media.local.actual.chat cdn.local.actual.chat

    set removeHostEntriesScript="(Get-Content "%hostsFile%") | where-object { $_ -notmatch '[0-9\.]+\s+%hosts%.*' } | Set-Content "%hostsFile%""
    powershell -Command %removeHostEntriesScript%;
    set addHostEntryScript="Add-Content -Path '%hostsFile%' -Value '%hostsLine%'"
    powershell -Command %addHostEntryScript%;
    echo hosts file patched

    echo trusting certificate...
    set wd=%~dp0
    certutil -addstore -f "ROOT" "%wd%.config\local.actual.chat\ssl\local.actual.chat.crt"

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
    certPath=.config/local.actual.chat/ssl/local.actual.chat.crt
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
localIp=$(ifconfig | grep "inet " | grep -Fv 127.0.0.1 | awk '{print $2}')
[ -z "$localIp" ] && echo "Failed to detect local ip address" && exit 1
updateHostsFile "$localIp" "local.actual.chat media.local.actual.chat cdn.local.actual.chat" "/etc/hosts"

echo trusting actual.chat certificate...
trustCertificate

echo installing dotnet dev certs...
dotnet dev-certs https --trust
