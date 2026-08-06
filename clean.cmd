:<<BATCH
    dotnet build-server shutdown
    ./run-build.cmd clean
    rmdir /S /Q artifacts\bin
    rmdir /S /Q artifacts\obj
    rmdir /S /Q artifacts\out
    rmdir /S /Q artifacts\publish
    rmdir /S /Q artifacts\tests
    rmdir /S /Q artifacts\claude-docker
    dotnet restore
    echo "Clean completed."
    exit /b
BATCH

#!/bin/sh
dotnet build-server shutdown
./run-build.cmd clean
rm -rf artifacts/bin
rm -rf artifacts/obj
rm -rf artifacts/out
rm -rf artifacts/publish
rm -rf artifacts/tests
rm -rf artifacts/claude-docker
dotnet restore
echo "Clean completed."
