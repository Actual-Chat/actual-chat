:<<BATCH
    @echo off
    if not exist tmp mkdir tmp
    set ASPNETCORE_ENVIRONMENT=Development
    set EnableAnalyzer=false
    set EnableNETAnalyzers=false
    dotnet watch run --project src/dotnet/App.Server 2>&1 | powershell -NoProfile -Command "$input | Tee-Object -FilePath tmp/watch-dotnet.log"
    exit /b %ERRORLEVEL%
BATCH

#!/bin/sh
mkdir -p tmp
ASPNETCORE_ENVIRONMENT=Development EnableAnalyzer=false EnableNETAnalyzers=false \
    dotnet watch run --project src/dotnet/App.Server 2>&1 | tee tmp/watch-dotnet.log
exit $?
