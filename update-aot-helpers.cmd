:<<BATCH
    @echo off
    dotnet run --project src/dotnet/App.AotHelper/App.AotHelper.csproj -c Debug -- -g %*
    exit /b %ERRORLEVEL%
BATCH

#!/bin/sh
dotnet run --project src/dotnet/App.AotHelper/App.AotHelper.csproj -c Debug -- -g "$@"
exit $?
