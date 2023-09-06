:<<BATCH
    @echo off

    set inDir=resources\sounds
    set outDir=src\dotnet\UI.Blazor\Services\TuneUI\sounds
    for %%F in (%inDir%\*.*) do (
        REM converting to MONO 48KHz
        ffmpeg -y -i %%F -ac 1 -ar 48000 %outDir%\%%~nF.webm %outDir%\%%~nF.mp3
    )
    exit /b
BATCH

#!/bin/sh
