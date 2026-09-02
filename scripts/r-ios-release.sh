#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT" || exit 1
source "$SCRIPT_DIR/__detect-ios-device.sh"

# Build IPA
"$REPO_ROOT/run-build.cmd" pack-ios --configuration Release --is-dev-maui "true"
if [ $? -ne 0 ]; then
    echo "Build failed"
    exit 1
fi

# Find the built app bundle
APP_PATH="$PUBLISH_DIR/Payload/ActualChat.app"
if [ ! -d "$APP_PATH" ]; then
    echo "Error: App bundle not found at $APP_PATH"
    exit 1
fi

# Install onto device
DeviceId=$(detect_ios_device)
echo "Installing app onto device..."
xcrun devicectl device install app --device "$DeviceId" "$APP_PATH"
if [ $? -ne 0 ]; then
    echo "Install failed"
    exit 1
fi

# Launch app on device
BUNDLE_ID="chat.actual.dev.app"
echo "Launching app #$BUNDLE_ID..."
xcrun devicectl device process launch --device "$DeviceId" "$BUNDLE_ID"
