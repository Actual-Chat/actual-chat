:<<BATCH
    @echo off
    dotnet run --project build -c Release -- watch %*
    exit /b %ERRORLEVEL%
BATCH

#!/bin/sh
dotnet run --project build -c Release -- watch "$@"
exit $?
