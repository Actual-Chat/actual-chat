#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT" || exit 1
source "$SCRIPT_DIR/__run-args.sh"
parse_run_args "$@"

# Mac Catalyst RID follows the host CPU.
case "$(uname -m)" in
    arm64) RID="maccatalyst-arm64" ;;
    *)     RID="maccatalyst-x64" ;;
esac

# Build the JS bundle, then the Mac Catalyst app.
[ -n "$MUST_SKIP_WEB" ] || npm run build:Debug || exit 1
dotnet build src/dotnet/App.Maui/ -f net11.0-maccatalyst -p:RuntimeIdentifier="$RID" "${BUILD_ARGS[@]}" || exit 1

# Dev and prod bundles differ in name and may sit side by side, so the name is exact.
APP_PATH="$REPO_ROOT/artifacts/bin/App.Maui/debug_net11.0-maccatalyst_${RID}/$MAC_APP_NAME"
if [ ! -d "$APP_PATH" ]; then
    echo "error: not found: $APP_PATH" >&2
    exit 1
fi
if [ -n "$IS_BUILD_ONLY" ]; then
    echo "Built: $APP_PATH"
    exit 0
fi

# Terminate a previous instance, then run the binary directly so its logs stream to this terminal.
pkill -f "$APP_PATH/Contents/MacOS/" 2>/dev/null
echo "Launching: $APP_PATH"
exec "$APP_PATH/Contents/MacOS/ActualChat"
