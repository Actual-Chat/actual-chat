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
    set hostsString=127.0.0.1  local.actual.chat media.local.actual.chat cdn.local.actual.chat
    set newLine=^& echo.

    find /C /I "%hostsString%" %hostsFile%
    if %ERRORLEVEL% NEQ 0 ECHO %newLine%^%hostsString%>>%hostsFile%
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

appendIfNotExists() {
  LINE=$1
  FILE=$2
  sudo touch $FILE
  sudo grep -qF -- "$LINE" "$FILE" || echo "$LINE" | sudo tee -a "$FILE"
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
appendIfNotExists "127.0.0.1  local.actual.chat media.local.actual.chat cdn.local.actual.chat" "/etc/hosts"

echo trusting actual.chat certificate...
trustCertificate

echo installing dotnet dev certs...
dotnet dev-certs https --trust
