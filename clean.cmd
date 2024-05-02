:<<BATCH
    dotnet build-server shutdown
    rmdir /S /Q artifacts\bin
    rmdir /S /Q artifacts\obj
    rmdir /S /Q artifacts\out
    rmdir /S /Q artifacts\repacked
    rmdir /S /Q artifacts\publish
    rmdir /S /Q artifacts\tests
    dotnet restore ActualChat.sln
    echo "Clean completed."
    exit /b
BATCH

#!/bin/sh
dotnet build-server shutdown
rmdir artifacts/bin
rmdir artifacts/obj
rmdir artifacts/out
rmdir artifacts/repacked
rmdir artifacts/publish
rmdir artifacts/tests
dotnet restore ActualChat.sln
echo "Clean completed."
