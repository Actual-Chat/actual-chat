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

    echo updating hosts and .env...
    pwsh -NoProfile -c ". ./scripts/Common.ps1; Update-HostEntries -DetectIP -Hostnames 'local.voxt.ai','media.local.voxt.ai','cdn.local.voxt.ai' | Out-Null; Update-HostEntries -DetectIP -Hostnames 'local.actual.chat','media.local.actual.chat','cdn.local.actual.chat' | Out-Null; Update-LocalIP | Out-Null"

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

trustCertificate() {
    certPath=.config/local.voxt.ai/ssl/local.voxt.ai.crt
    case `uname` in
      Darwin)
        sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain $certPath
      ;;
      Linux)
        sudo cp $certPath /usr/local/share/ca-certificates/
        sudo update-ca-certificates
      ;;
      *)
        echo "Not supported OS!" 1>&2
        exit 1
      ;;
    esac
}

echo updating hosts and .env...
pwsh -NoProfile -c ". ./scripts/Common.ps1; Update-HostEntries -DetectIP -Hostnames 'local.voxt.ai','media.local.voxt.ai','cdn.local.voxt.ai' | Out-Null; Update-HostEntries -DetectIP -Hostnames 'local.actual.chat','media.local.actual.chat','cdn.local.actual.chat' | Out-Null; Update-LocalIP | Out-Null"

echo trusting voxt.ai certificate...
trustCertificate

echo installing dotnet dev certs...
dotnet dev-certs https --trust
