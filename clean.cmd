:<<BATCH
    @echo off
    if exist "artifacts" rmdir /s /q "artifacts"
    if exist "src/nodejs/node_modules" rmdir /s /q "src/nodejs/node_modules"
    if exist "src/dotnet/ClientApp/wwwroot/dist" rmdir /s /q "src/dotnet/ClientApp/wwwroot/dist"
    if exist "src/dotnet/UI.Blazor.Host/wwwroot/dist" rmdir /s /q "src/dotnet/UI.Blazor.Host/wwwroot/dist"
    echo Clean completed.

    exit /b
BATCH

#!/bin/sh
rm -rf "artifacts" "src/nodejs/node_modules" "src/dotnet/*/wwwroot/dist"
echo Clean completed.
