@echo off
setlocal

pushd "%~dp0"
if not exist tmp mkdir tmp

set ActualChat_DevLog=%CD%\tmp\server.log
set ASPNETCORE_ENVIRONMENT=Development

dotnet run --configuration Debug --no-launch-profile --project src/dotnet/App.Server/App.Server.csproj

popd
