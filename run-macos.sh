#!/bin/bash
# Build and run the Mac Catalyst app locally (Debug), mirroring run-ios.sh.
# See docs/maccatalyst-distribution.md for signing prerequisites.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR" || exit 1

IS_DEV_MAUI="true"   # dev backend (dev.voxt.ai) -> "Voxt (Dev).app"
BUILD_JS="true"
CONSOLE="false"

for arg in "$@"; do
    case "$arg" in
        --prod) IS_DEV_MAUI="false" ;;        # prod backend (voxt.ai) -> "Voxt.app"
        --no-js) BUILD_JS="false" ;;          # skip the TypeScript/wwwroot build
        --console) CONSOLE="true" ;;          # run in foreground, stream stdout logs
        *) echo "Unknown option: $arg" >&2; echo "Usage: ./run-macos.sh [--prod] [--no-js] [--console]" >&2; exit 1 ;;
    esac
done

if [ "$(uname -m)" = "arm64" ]; then
    RID="maccatalyst-arm64"
else
    RID="maccatalyst-x64"
fi

if [ "$IS_DEV_MAUI" = "true" ]; then
    APP_NAME="Voxt (Dev).app"
else
    APP_NAME="Voxt.app"
fi
APP_PATH="$SCRIPT_DIR/artifacts/bin/App.Maui/debug_net10.0-maccatalyst_${RID}/$APP_NAME"

if [ "$BUILD_JS" = "true" ]; then
    npm run build:Debug || exit 1
fi

dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net10.0-maccatalyst -c Debug \
    -p:IsDevMaui=$IS_DEV_MAUI -p:RuntimeIdentifier=$RID || exit 1

# Quit any running instance so the fresh build takes its place.
osascript -e "quit app \"${APP_NAME%.app}\"" 2>/dev/null
pkill -f "$APP_NAME/Contents/MacOS/ActualChat" 2>/dev/null
sleep 1

if [ ! -d "$APP_PATH" ]; then
    echo "Error: built app not found at $APP_PATH" >&2
    exit 1
fi

if [ "$CONSOLE" = "true" ]; then
    echo "Launching $APP_NAME (foreground, live logs)..."
    exec "$APP_PATH/Contents/MacOS/ActualChat"
else
    echo "Launching $APP_NAME..."
    open "$APP_PATH"
fi
