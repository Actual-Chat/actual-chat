:<<BATCH
    @echo off

    set ASPNETCORE_ENVIRONMENT=Development
    rem set ASPNETCORE_URLS=https://localhost:7080;https://localhost:7081

    rem start cmd /C timeout 5 ^& start https://local.voxt.ai"
    pushd artifacts\publish\App.Server\release
    ActualChat.App.Server.exe
    popd
    exit /b
BATCH

#!/bin/sh
export ASPNETCORE_ENVIRONMENT=Development
# export ASPNETCORE_URLS="https://localhost:7080;https://localhost:7081"

# (sleep 5 && open https://local.voxt.ai || xdbg-open https://local.voxt.ai) &

pushd artifacts/publish/App.Server/release
./ActualChat.App.Server %*
popd
