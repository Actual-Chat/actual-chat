:<<BATCH
    @echo off

    set inDir=resources\sounds\raw
    set outDir=resources\sounds\converted
    for %%F in (%inDir%\*.*) do (
        ffmpeg -y -i %%F -ac:0 1 -ar:0 48000 %outDir%\%%~nF.webm
        ffmpeg -y -i %%F -ac:0 1 -ar:0 48000 -f mp4 -c aac -b:a 64k %outDir%\%%~nF.m4a
        ffmpeg -y -i %%F -ac 1 -ar 48000 -f caf -c:a adpcm_ima_wav %outDir%\%%~nF.caf
    )
    exit /b
BATCH

#!/bin/sh

inDir="resources/sounds/raw"
outDir="resources/sounds/converted"

# Create output directory if it doesn't exist
mkdir -p "$outDir"

# Process each file in the input directory
for file in "$inDir"/*; do
    # Skip if not a file
    [ ! -f "$file" ] && continue

    # Get filename without extension
    filename=$(basename "$file")
    name="${filename%.*}"

    ffmpeg -y -i "$file" -ac:0 1 -ar:0 48000 "$outDir/${name}.webm"
    ffmpeg -y -i "$file" -ac:0 1 -ar:0 48000 -f mp4 -c aac -b:a 64k "$outDir/${name}.m4a"
    ffmpeg -y -i "$file" -ac 1 -ar 48000 -f caf -c:a adpcm_ima_wav "$outDir/${name}.caf"
done
