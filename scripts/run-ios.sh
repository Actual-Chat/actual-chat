#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT" || exit 1
source "$SCRIPT_DIR/__detect-ios-device.sh"

DEVICE_UDID="$(detect_ios_device)"
APP_PATH="$REPO_ROOT/artifacts/bin/App.Maui/debug_net11.0-ios_ios-arm64/ActualChat.app"

# Build only (mlaunch Run fails on iOS 26.3+)
npm run build:Debug
dotnet build src/dotnet/App.Maui/ -f net11.0-ios -p:RuntimeIdentifier=ios-arm64 || exit 1

# Install and launch via devicectl
xcrun devicectl device install app --device "$DEVICE_UDID" "$APP_PATH" || exit 1
xcrun devicectl device process launch --device "$DEVICE_UDID" --terminate-existing chat.actual.dev.app
