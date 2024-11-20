:<<BATCH
    @echo off
    dotnet publish src/dotnet/App.Server/App.Server.csproj -c:Release -p:UseAppPack=false -- %*

    exit /b
BATCH

#!/bin/sh
dotnet publish src/dotnet/App.Server/App.Server.csproj -c:Release -p:UseAppPack=false -- "$@"
