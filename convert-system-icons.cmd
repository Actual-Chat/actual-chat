:<<BATCH
    @echo off
    setlocal enabledelayedexpansion

    where rsvg-convert >nul 2>&1
    if errorlevel 1 (
        echo ERROR: rsvg-convert not found in PATH.
        echo Install with one of:
        echo   scoop install librsvg
        echo   choco install rsvg
        echo   winget install librsvg
        exit /b 1
    )

    set inDir=src\dotnet\Media.Service\Resources
    set outDir=%inDir%\Converted
    if not exist "%outDir%" mkdir "%outDir%"

    for %%F in (%inDir%\*.svg) do (
        echo Converting %%~nxF
        rsvg-convert -w 512 -h 512 -o %outDir%\%%~nF.png %%F || exit /b 1
    )

    for %%F in (%outDir%\*.png) do (
        if not exist "%inDir%\%%~nF.svg" (
            echo Removing stale %%~nxF
            del "%outDir%\%%~nxF" || exit /b 1
        )
    )
    exit /b 0
BATCH

#!/bin/sh
set -eu

if ! command -v rsvg-convert >/dev/null 2>&1; then
    cat >&2 <<EOF
ERROR: rsvg-convert not found in PATH.
Install with one of:
  apt install librsvg2-bin       # Debian / Ubuntu
  dnf install librsvg2-tools     # Fedora
  brew install librsvg           # macOS
EOF
    exit 1
fi

inDir="src/dotnet/Media.Service/Resources"
outDir="$inDir/Converted"
mkdir -p "$outDir"

for svg in "$inDir"/*.svg; do
    name=$(basename "$svg" .svg)
    echo "Converting ${name}.svg"
    rsvg-convert -w 512 -h 512 -o "$outDir/${name}.png" "$svg"
done

for png in "$outDir"/*.png; do
    [ -e "$png" ] || continue
    name=$(basename "$png" .png)
    if [ ! -f "$inDir/${name}.svg" ]; then
        echo "Removing stale ${name}.png"
        rm -- "$png"
    fi
done
