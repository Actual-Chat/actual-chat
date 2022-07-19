@echo off
dotnet publish -c Release -o ./artifacts/app ./src/dotnet/Host/Host.csproj

set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=https://localhost:7080;https://localhost:7081
pushd "artifacts/app"
start cmd /C timeout 5 ^& start https://localhost:7081/"
start cmd /C ActualChat.App.Server.exe
popd
