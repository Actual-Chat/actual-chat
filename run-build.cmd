:<<BATCH
    @echo off
    dotnet run --project build -- %*

    exit /b
BATCH

#!/bin/sh
dotnet run --project build -- "$@"