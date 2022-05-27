@echo off

set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=https://localhost:7080;https://localhost:7081

start cmd /C timeout 5 ^& start https://localhost:7081/"
pushd artifacts\bin
ActualChat.Host.exe
popd
