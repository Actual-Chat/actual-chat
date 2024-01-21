@echo off
if "%1" == "--build-js" (
    call npm-build
)
dotnet build ActualChat.CI.slnf

set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://localhost:7080

start cmd /C timeout 10 ^& start https://local.actual.chat/"
start cmd /C dotnet run --no-launch-profile --project src/dotnet/App.Server/App.Server.csproj
