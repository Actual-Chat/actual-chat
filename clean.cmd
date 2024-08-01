:<<BATCH
    dotnet build-server shutdown
    ./run-build.cmd clean
    rmdir /S /Q artifacts\bin
    rmdir /S /Q artifacts\obj
    rmdir /S /Q artifacts\out
    rmdir /S /Q artifacts\repacked
    rmdir /S /Q artifacts\publish
    rmdir /S /Q artifacts\tests
    echo "Clean completed."
    exit /b
BATCH

#!/bin/sh
dotnet build-server shutdown
./run-build.cmd clean
rmdir artifacts/bin
rmdir artifacts/obj
rmdir artifacts/out
rmdir artifacts/repacked
rmdir artifacts/publish
rmdir artifacts/tests
echo "Clean completed."
