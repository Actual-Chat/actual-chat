:<<BATCH
    @echo off
    pwsh -NoProfile -File "%~dp0b.ps1" %*
    exit /b %ERRORLEVEL%
BATCH

#!/bin/sh
exec pwsh -NoProfile -File "$(dirname "$0")/b.ps1" "$@"
