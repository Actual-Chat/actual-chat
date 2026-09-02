#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT" || exit 1

# Mac Catalyst RID follows the host CPU.
case "$(uname -m)" in
    arm64) RID="maccatalyst-arm64" ;;
    *)     RID="maccatalyst-x64" ;;
esac

# Build the JS bundle, then the Mac Catalyst app.
npm run build:Debug || exit 1
dotnet build src/dotnet/App.Maui/ -f net11.0-maccatalyst -p:RuntimeIdentifier="$RID" || exit 1

# The produced bundle name depends on IsDevMaui ("Voxt (Dev).app" for dev, "Voxt.app" for prod).
OUT_DIR="$REPO_ROOT/artifacts/bin/App.Maui/debug_net11.0-maccatalyst_${RID}"
APP_PATH="$(ls -d "$OUT_DIR"/*.app 2>/dev/null | head -1)"
if [ -z "$APP_PATH" ]; then
    echo "error: no .app bundle found in $OUT_DIR" >&2
    exit 1
fi

# Terminate a previous instance, then run the binary directly so its logs stream to this terminal.
pkill -f "$APP_PATH/Contents/MacOS/" 2>/dev/null
echo "Launching: $APP_PATH"
exec "$APP_PATH/Contents/MacOS/ActualChat"
