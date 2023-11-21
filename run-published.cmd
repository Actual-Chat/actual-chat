:<<BATCH
    @echo off

    set ASPNETCORE_ENVIRONMENT=Development
    rem set ASPNETCORE_URLS=https://localhost:7080;https://localhost:7081

    start cmd /C timeout 5 ^& start https://local.actual.chat"
    pushd artifacts\publish\App.Server\release
    ActualChat.App.Server.exe
    popd
    exit /b
BATCH

#!/bin/sh
export ASPNETCORE_ENVIRONMENT=Development
# export ASPNETCORE_URLS="https://localhost:7080;https://localhost:7081"

(sleep 5 && open https://local.actual.chat || xdbg-open https://local.actual.chat) &

pushd artifacts/publish/App.Server/release
./ActualChat.App.Server
popd
