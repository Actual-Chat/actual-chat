:<<BATCH
    @echo off
    pwsh -NoProfile -File "%~dp0scripts\Server-PortCheck.ps1" -ProjectPath "%~dp0." -Kill %*
    exit /b %ERRORLEVEL%
BATCH

#!/bin/sh
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
pwsh -NoProfile -File "$SCRIPT_DIR/scripts/Server-PortCheck.ps1" -ProjectPath "$SCRIPT_DIR" -Kill "$@"
exit $?
