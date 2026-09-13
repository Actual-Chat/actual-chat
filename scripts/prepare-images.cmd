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
    set iconDir=src\nodejs\icons
    set outDir=resources\images\converted
    if not exist "%outDir%" mkdir "%outDir%"

    for %%N in (error-cat-dark error-cat-light share-cat-dark share-cat-light) do (
        call :rasterize %inDir%\%%N.svg %%N || exit /b 1
    )
    for %%N in (message-ellipse) do (
        call :rasterize %iconDir%\%%N.svg %%N || exit /b 1
    )
    exit /b 0

    :rasterize
    echo Converting %1
    rsvg-convert -o %outDir%\%2.png %1 || exit /b 1
    rsvg-convert -z 2 -o %outDir%\%2@2x.png %1 || exit /b 1
    rsvg-convert -z 3 -o %outDir%\%2@3x.png %1 || exit /b 1
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
# web, which is served every other file under $inDir as-is. A new name also needs an .imageset
# - Contents.json plus an ImageAsset link - in every project that renders it.
# @2x/@3x come from the SVG's own size, so each image keeps its dimensions.
# $iconDir holds the icon font's glyphs; those image sets are template images.
names="error-cat-dark error-cat-light share-cat-dark share-cat-light"
iconNames="message-ellipse"

inDir="src/nodejs/images"
iconDir="src/nodejs/icons"
outDir="resources/images/converted"
mkdir -p "$outDir"

rasterize() {
    echo "Converting $1"
    rsvg-convert      -o "$outDir/$2.png"    "$1"
    rsvg-convert -z 2 -o "$outDir/$2@2x.png" "$1"
    rsvg-convert -z 3 -o "$outDir/$2@3x.png" "$1"
}

for name in $names; do
    rasterize "$inDir/${name}.svg" "$name"
done
for name in $iconNames; do
    rasterize "$iconDir/${name}.svg" "$name"
done
