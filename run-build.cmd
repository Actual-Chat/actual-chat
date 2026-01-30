:<<BATCH
    @echo off
    dotnet run --project build -c Release -- %*
    exit /b %ERRORLEVEL%
BATCH

#!/bin/sh
dotnet run --project build -c Release -- "$@"
exit $?