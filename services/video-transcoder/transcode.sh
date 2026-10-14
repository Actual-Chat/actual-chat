#!/bin/sh
# A Cloud Run Job execution started by GoogleCloudVideoUploadProcessor. The arguments are ffmpeg's
# output options; INPUT_URL and OUTPUT_URL are signed GCS URLs to read the source and PUT the MP4.
# Without INPUT_URL it only prints the ffmpeg version, which is enough to time an execution's start.
set -eu

if [ -z "${INPUT_URL:-}" ]; then
    ffmpeg -hide_banner -version | head -n 1
    exit 0
fi

output=/tmp/output.mp4
ffmpeg -hide_banner -nostdin -loglevel warning -stats -y -i "$INPUT_URL" "$@" "$output"
curl --fail --silent --show-error --retry 3 \
    -X PUT -H "Content-Type: video/mp4" --upload-file "$output" "$OUTPUT_URL"
