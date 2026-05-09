:<<BATCH
    @echo off
    if /i "%~1"=="-f" goto :skip_ci
    if /i "%~1"=="--fast" goto :skip_ci
    call npm ci
    :skip_ci
    call npm run build:Debug
    exit /b
BATCH

#!/bin/sh
# "./npm-install.cmd"
if [ "$1" != "-f" ] && [ "$1" != "--fast" ]; then
    npm ci
fi
npm run build:Debug
