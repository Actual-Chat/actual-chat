:<<BATCH
    @echo off
    dotnet run --project build -c Release -- %*

    exit /b
BATCH

#!/bin/sh
dotnet run --project build -c Release -- "$@"