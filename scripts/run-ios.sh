#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT" || exit 1
source "$SCRIPT_DIR/__detect-ios-device.sh"
source "$SCRIPT_DIR/__run-args.sh"
parse_run_args "$@"

# Finds a booted simulator or boots the newest available iPhone one. Sets SIMULATOR_UDID.
find_simulator() {
    local name
    SIMULATOR_UDID=$(xcrun simctl list devices booted -j 2>/dev/null | python3 -c "
import json, sys
data = json.load(sys.stdin)
for runtime, devices in data.get('devices', {}).items():
    for d in devices:
        if d.get('state') == 'Booted':
            print(d['udid'])
            sys.exit(0)
" 2>/dev/null)

    if [ -n "$SIMULATOR_UDID" ]; then
        name=$(xcrun simctl list devices booted -j 2>/dev/null | python3 -c "
import json, sys
data = json.load(sys.stdin)
for runtime, devices in data.get('devices', {}).items():
    for d in devices:
        if d.get('state') == 'Booted':
            print(d.get('name', 'Unknown'))
            sys.exit(0)
" 2>/dev/null)
        echo "Using booted simulator: $name ($SIMULATOR_UDID)"
        return
    fi

    echo "No booted simulator found. Searching for an available iPhone simulator..."
    local info
    info=$(xcrun simctl list devices available -j 2>/dev/null | python3 -c "
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
    if [ -z "$info" ]; then
        echo "Error: No available iPhone simulator found." >&2
        echo "" >&2
        echo "Available simulators:" >&2
        xcrun simctl list devices available >&2
        exit 1
    fi

    SIMULATOR_UDID="${info%%|*}"
    name="${info##*|}"
    echo "Booting simulator: $name ($SIMULATOR_UDID)"
    xcrun simctl boot "$SIMULATOR_UDID"
    open -a Simulator
}

# Installs the local root CA certificate, once per simulator and again when the certificate changes.
install_simulator_root_cert() {
    local cert_path="$REPO_ROOT/.config/local.voxt.ai/ssl/rootCA.crt"
    local cert_marker="$HOME/.ios-simulator-certs/$SIMULATOR_UDID"
    if [ ! -f "$cert_path" ]; then
        return
    fi
    if [ -f "$cert_marker" ] && [ ! "$cert_path" -nt "$cert_marker" ]; then
        return
    fi

    echo "Installing root CA certificate..."
    if xcrun simctl keychain "$SIMULATOR_UDID" add-root-cert "$cert_path"; then
        mkdir -p "$(dirname "$cert_marker")"
        touch "$cert_marker"
    fi
}

# The codesigning step of a simulator build sometimes corrupts dylib files in the app bundle
# by writing command-line args instead of signing; this restores them from the build output.
fix_corrupted_dylibs() {
    local dylib filename source_dylib
    for dylib in "$APP_PATH"/*.dylib; do
        [ -f "$dylib" ] || continue
        if file "$dylib" | grep -q "Mach-O"; then
            continue
        fi

        filename=$(basename "$dylib")
        source_dylib="$OUT_DIR/$filename"
        echo "Fixing corrupted library: $filename"
        if [ -f "$source_dylib" ] && file "$source_dylib" | grep -q "Mach-O"; then
            cp "$source_dylib" "$dylib"
            codesign --force --sign - "$dylib"
        else
            echo "Warning: Could not find valid source for $filename"
        fi
    done
}

# The target decides the runtime identifier, so it is resolved before the build.
if [ -z "$IS_SIMULATOR" ]; then
    RID="ios-arm64"
elif [ "$(uname -m)" = "arm64" ]; then
    RID="iossimulator-arm64"
else
    RID="iossimulator-x64"
fi
OUT_DIR="$REPO_ROOT/artifacts/bin/App.Maui/debug_net11.0-ios_$RID"
APP_PATH="$OUT_DIR/ActualChat.app"
BUNDLE_ID="chat.actual.dev.app"

# A build needs no device or simulator.
DEVICE_ARGS=()
if [ -z "$IS_BUILD_ONLY" ]; then
    if [ -n "$IS_SIMULATOR" ]; then
        find_simulator
        install_simulator_root_cert
        DEVICE_ARGS=("-p:_DeviceName=:v2:udid=$SIMULATOR_UDID")
    else
        DEVICE_UDID="$(detect_ios_device)" || exit 1
    fi
fi

if [ -z "$MUST_SKIP_WEB" ]; then
    npm run build:Debug || exit 1
fi
# Device and simulator builds share one intermediate folder (IntermediateOutputPath in App.Maui.csproj),
# and the objects of one don't link into the other, so it is cleared whenever the target changes.
OBJ_DIR="$REPO_ROOT/artifacts/out"
if [ "$(cat "$OBJ_DIR/.rid" 2>/dev/null)" != "$RID" ]; then
    rm -rf "$OBJ_DIR"
    mkdir -p "$OBJ_DIR"
    echo "$RID" > "$OBJ_DIR/.rid"
fi
# Build only: mlaunch Run fails on iOS 26.3+, and on a simulator it may re-corrupt the dylibs
dotnet build src/dotnet/App.Maui/ -f net11.0-ios -p:RuntimeIdentifier="$RID" \
    "${DEVICE_ARGS[@]}" "${BUILD_ARGS[@]}" || exit 1
if [ -n "$IS_SIMULATOR" ]; then
    fix_corrupted_dylibs
fi
if [ -n "$IS_BUILD_ONLY" ]; then
    echo "Built: $APP_PATH"
    exit 0
fi

if [ -n "$IS_SIMULATOR" ]; then
    xcrun simctl install "$SIMULATOR_UDID" "$APP_PATH" || exit 1
else
    xcrun devicectl device install app --device "$DEVICE_UDID" "$APP_PATH" || exit 1
fi
if [ -n "$MUST_SKIP_LAUNCH" ]; then
    echo "Installed: $BUNDLE_ID"
    exit 0
fi

# --console keeps the script alive until the app exits and streams its output
if [ -n "$IS_SIMULATOR" ]; then
    xcrun simctl launch --console --terminate-running-process "$SIMULATOR_UDID" "$BUNDLE_ID"
else
    xcrun devicectl device process launch --console --device "$DEVICE_UDID" --terminate-existing "$BUNDLE_ID"
fi
