@echo off
dotnet publish --no-restore --nologo -c Release -nodeReuse:false -o ./artifacts/app ./src/dotnet/Host/Host.csproj

set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://localhost:7080/
pushd "artifacts/app"
start cmd /C timeout 5 ^& start http://localhost:7080/"
start cmd /C ActualChat.Host.exe
popd
