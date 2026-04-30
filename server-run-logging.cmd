@echo off
setlocal

pushd "%~dp0"
if not exist tmp mkdir tmp

set ActualChat_DevLog=%CD%\tmp\server.log
set ASPNETCORE_ENVIRONMENT=Development

if exist "%ActualChat_DevLog%" del "%ActualChat_DevLog%"
dotnet run --configuration Debug --no-launch-profile --project src/dotnet/App.Server/App.Server.csproj

popd
