#!/bin/bash

# Detect connected iOS device (using xctrace which returns UDID)
# Returns: DeviceId (UDID)
# Exits with error if no device found

detect_ios_device() {
    local deviceLine=$(xcrun xctrace list devices 2>/dev/null | sed -n '/^== Devices ==$/,/^==/p' | grep -E '\([0-9]+\.[0-9]+' | head -1)

    if [ -z "$deviceLine" ]; then
        echo "Error: No available iOS device found. Please connect an iPhone and ensure it's paired." >&2
        echo "" >&2
        echo "Available devices:" >&2
        xcrun xctrace list devices 2>/dev/null >&2
        exit 1
    fi

    local deviceId=$(echo "$deviceLine" | sed 's/.*(\([^)]*\))$/\1/')
    local deviceName=$(echo "$deviceLine" | sed 's/ ([^)]*) ([^)]*)$//')

    echo "Using device: $deviceName ($deviceId)" >&2
    echo "$deviceId"
}
