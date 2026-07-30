#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Detect architecture
ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
    RID="iossimulator-arm64"
else
    RID="iossimulator-x64"
fi

# Find a booted simulator, or boot one
BOOTED_UDID=$(xcrun simctl list devices booted -j 2>/dev/null | python3 -c "
import json, sys
data = json.load(sys.stdin)
for runtime, devices in data.get('devices', {}).items():
    for d in devices:
        if d.get('state') == 'Booted':
            print(d['udid'])
            sys.exit(0)
" 2>/dev/null)

if [ -z "$BOOTED_UDID" ]; then
    echo "No booted simulator found. Searching for an available iPhone simulator..."
    SIM_INFO=$(xcrun simctl list devices available -j 2>/dev/null | python3 -c "
import json, sys
data = json.load(sys.stdin)
for runtime, devices in sorted(data.get('devices', {}).items(), reverse=True):
    if 'iOS' not in runtime:
        continue
    for d in devices:
        name = d.get('name', '')
        if 'iPhone' in name and d.get('isAvailable', False):
            print(d['udid'] + '|' + name)
            sys.exit(0)
print('')
" 2>/dev/null)

    if [ -z "$SIM_INFO" ]; then
        echo "Error: No available iPhone simulator found." >&2
        echo "" >&2
        echo "Available simulators:" >&2
        xcrun simctl list devices available >&2
        exit 1
    fi

    BOOTED_UDID="${SIM_INFO%%|*}"
    SIM_NAME="${SIM_INFO##*|}"
    echo "Booting simulator: $SIM_NAME ($BOOTED_UDID)"
    xcrun simctl boot "$BOOTED_UDID"
    open -a Simulator
else
    SIM_NAME=$(xcrun simctl list devices booted -j 2>/dev/null | python3 -c "
import json, sys
data = json.load(sys.stdin)
for runtime, devices in data.get('devices', {}).items():
    for d in devices:
        if d.get('state') == 'Booted':
            print(d.get('name', 'Unknown'))
            sys.exit(0)
" 2>/dev/null)
    echo "Using booted simulator: $SIM_NAME ($BOOTED_UDID)"
fi

# Install root CA certificate if not already done for this simulator
CERT_PATH="$SCRIPT_DIR/.config/local.voxt.ai/ssl/rootCA.crt"
CERT_MARKER="$HOME/.ios-simulator-certs/$BOOTED_UDID"

if [ -f "$CERT_PATH" ]; then
    if [ ! -f "$CERT_MARKER" ] || [ "$CERT_PATH" -nt "$CERT_MARKER" ]; then
        echo "Installing root CA certificate..."
        xcrun simctl keychain "$BOOTED_UDID" add-root-cert "$CERT_PATH"
        if [ $? -eq 0 ]; then
            mkdir -p "$(dirname "$CERT_MARKER")"
            touch "$CERT_MARKER"
        fi
    fi
fi

# Clean stale native artifacts from physical device builds to avoid linker errors
rm -rf "$SCRIPT_DIR/artifacts/out/nativelibraries"

# Build first (without Run) to allow fixing corrupted native libraries
dotnet build src/dotnet/App.Maui/ -f net11.0-ios -p:RuntimeIdentifier=$RID -p:_DeviceName=":v2:udid=$BOOTED_UDID"

if [ $? -ne 0 ]; then
    echo "Build failed"
    exit 1
fi

# Fix corrupted native libraries in the app bundle
# The codesigning step sometimes corrupts dylib files by writing command-line args instead of signing
APP_BUNDLE="$SCRIPT_DIR/artifacts/bin/App.Maui/debug_net11.0-ios_$RID/ActualChat.app"
BUILD_OUTPUT="$SCRIPT_DIR/artifacts/bin/App.Maui/debug_net11.0-ios_$RID"
BUNDLE_ID="chat.actual.dev.app"

for dylib in "$APP_BUNDLE"/*.dylib; do
    if [ -f "$dylib" ]; then
        filename=$(basename "$dylib")
        source_dylib="$BUILD_OUTPUT/$filename"
        # Check if the file is corrupted (not a valid Mach-O binary)
        if ! file "$dylib" | grep -q "Mach-O"; then
            echo "Fixing corrupted library: $filename"
            if [ -f "$source_dylib" ] && file "$source_dylib" | grep -q "Mach-O"; then
                cp "$source_dylib" "$dylib"
                # Re-sign the fixed library
                codesign --force --sign - "$dylib"
            else
                echo "Warning: Could not find valid source for $filename"
            fi
        fi
    fi
done

# Install and launch the app directly (bypassing dotnet Run target which may re-corrupt libraries)
echo "Installing app to simulator..."
xcrun simctl install "$BOOTED_UDID" "$APP_BUNDLE"

echo "Launching app..."
xcrun simctl launch --console --terminate-running-process "$BOOTED_UDID" "$BUNDLE_ID"
