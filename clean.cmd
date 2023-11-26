:<<BATCH
    dotnet build-server shutdown
    rmdir /S /Q artifacts
    dotnet restore ActualChat.Maui.sln
    echo "Clean completed."
    exit /b
BATCH

#!/bin/sh
dotnet build-server shutdown
rmdir artifacts
dotnet restore ActualChat.Maui.sln
echo "Clean completed."
