#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/__detect-ios-device.sh"

dotnet build src/dotnet/App.Maui/ -f net10.0-ios -p:RuntimeIdentifier=ios-arm64 -p:_DeviceName="$(detect_ios_device)" -t:"Build;Run"
