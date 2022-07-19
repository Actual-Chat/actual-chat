:<<BATCH
    @echo off

    set ASPNETCORE_ENVIRONMENT=Development
    set ASPNETCORE_URLS=https://localhost:7080;https://localhost:7081

    start cmd /C timeout 5 ^& start https://localhost:7081/"
    pushd artifacts\bin
    ActualChat.App.Server.exe
    popd
    exit /b
BATCH

#!/bin/sh
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="https://localhost:7080;https://localhost:7081"

(sleep 5 && open https://localhost:7081 || xdbg-open https://localhost:7081) &

pushd artifacts/bin
./ActualChat.App.Server
popd
