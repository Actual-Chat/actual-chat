:<<BATCH
    @echo off
    if exist "artifacts" rmdir /s /q "artifacts"
    if exist "src/nodejs/node_modules" rmdir /s /q "src/nodejs/node_modules"
    if exist "src/dotnet/App.Wasm/wwwroot/dist" rmdir /s /q "src/dotnet/App.Wasm/wwwroot/dist"
    if exist "src/dotnet/App.Maui/wwwroot/dist" rmdir /s /q "src/dotnet/App.Maui/wwwroot/dist"
    dotnet build-server shutdown
    echo Clean completed.

    exit /b
BATCH

#!/bin/sh
rm -rf artifacts src/nodejs/node_modules src/dotnet/*/wwwroot/dist **/obj/
dotnet build-server shutdown
echo Clean completed.
