:<<BATCH
    @echo off

    set inDir=resources\sounds
    set outDir=src\dotnet\UI.Blazor\Services\TuneUI\sounds
    for %%F in (%inDir%\*.*) do (
        REM converting to MONO 48KHz
        ffmpeg -y -i %%F -ac:0 1 -ar:0 48000 %outDir%\%%~nF.webm
        ffmpeg -y -i %%F -ac:0 1 -ar:0 48000 -f mp4 -c aac -b:a 64k %outDir%\%%~nF.m4a
    )
    exit /b
BATCH

#!/bin/sh
