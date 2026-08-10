:<<BATCH
    @echo off
    dotnet run --project src/dotnet/App.AotHelper/App.AotHelper.csproj -c Debug -- -g %*
    if errorlevel 1 exit /b %ERRORLEVEL%
    dotnet run --project src/dotnet/App.AotHelper/App.AotHelper.csproj -c Debug -- -m src/dotnet/App.Maui/_Profiling/aothelper.mibc
    exit /b %ERRORLEVEL%
BATCH

#!/bin/sh
dotnet run --project src/dotnet/App.AotHelper/App.AotHelper.csproj -c Debug -- -g "$@" || exit $?
dotnet run --project src/dotnet/App.AotHelper/App.AotHelper.csproj -c Debug -- -m src/dotnet/App.Maui/_Profiling/aothelper.mibc
exit $?
