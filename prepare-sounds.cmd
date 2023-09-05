:<<BATCH
    @echo off

    pushd src\dotnet\UI.Blazor\Services\TuneUI\sounds
    for %%F in (*.webm) do (
        REM converting to MONO 48KHz
        ffmpeg -y -i %%F -ac 1 -ar 48000 %%~nF.converted.webm
        ffmpeg -y -i %%F -ac 1 -ar 48000 %%~nF.mp3
        del %%F
        ren %%~nF.converted.webm %%F
    )
    popd
    exit /b
BATCH

#!/bin/sh
