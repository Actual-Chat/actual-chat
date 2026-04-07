:<<BATCH
    @echo off
    pwsh -NoProfile -c ". ./scripts/Common.ps1; . ./add-hosts.ps1; Configure-LocalEnvHosts"
    set "exitCode=%errorlevel%"
    pause
    exit /b %exitCode%
BATCH

#!/bin/sh
exec pwsh -NoProfile -c ". ./scripts/Common.ps1; . ./add-hosts.ps1; Configure-LocalEnvHosts"
