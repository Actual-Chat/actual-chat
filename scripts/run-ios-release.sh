#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT" || exit 1
source "$SCRIPT_DIR/__detect-ios-device.sh"
dotnet build -c Release src/dotnet/App.Maui/ -f net11.0-ios -p:RuntimeIdentifier=ios-arm64 -p:_DeviceName="$(detect_ios_device)" -t:"Build;Run"
