@echo off
dotnet build

set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=https://localhost:7080;https://localhost:7081

start cmd /C timeout 5 ^& start https://localhost:7081/"
start cmd /C dotnet run --no-launch-profile --project src/dotnet/Host/Host.csproj
