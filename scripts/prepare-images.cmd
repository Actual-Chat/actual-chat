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

    set inDir=src\nodejs\images
    set outDir=resources\images\converted
    if not exist "%outDir%" mkdir "%outDir%"

    for %%N in (error-barrier-image upload-progress-image) do (
        echo Converting %%N.svg
        rsvg-convert -o %outDir%\%%N.png %inDir%\%%N.svg || exit /b 1
        rsvg-convert -z 2 -o %outDir%\%%N@2x.png %inDir%\%%N.svg || exit /b 1
        rsvg-convert -z 3 -o %outDir%\%%N@3x.png %inDir%\%%N.svg || exit /b 1
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

# Only the SVGs the iOS app extension bundles as PNGs - UIKit can't render SVG, unlike the
# web, which is served every other file under $inDir as-is. Add a name here when a native
# view needs one. @2x/@3x come from the SVG's own size, so each image keeps its dimensions.
names="error-barrier-image upload-progress-image"

inDir="src/nodejs/images"
outDir="resources/images/converted"
mkdir -p "$outDir"

for name in $names; do
    echo "Converting ${name}.svg"
    rsvg-convert      -o "$outDir/${name}.png"    "$inDir/${name}.svg"
    rsvg-convert -z 2 -o "$outDir/${name}@2x.png" "$inDir/${name}.svg"
    rsvg-convert -z 3 -o "$outDir/${name}@3x.png" "$inDir/${name}.svg"
done
