#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT" || exit 1

# Debug signing identity: the developer's own Apple Development cert issued under the team.
# Passed as a SHA-1 hash so a keychain with several Apple Development certs stays unambiguous;
# with none found the csproj default ("Apple Development") applies.
TEAM_ID="M287G8G83F"
CODESIGN_ARGS=()
while read -r hash; do
    subject="$(security find-certificate -a -Z -p -c "Apple Development" 2>/dev/null \
        | awk -v h="$hash" '/^SHA-1 hash:/ { m = ($3 == h) } m && /BEGIN CERT/ { p = 1 } p { print } /END CERT/ { p = 0 }' \
        | openssl x509 -noout -subject 2>/dev/null)"
    case "$subject" in
        *"OU=$TEAM_ID"*|*"OU = $TEAM_ID"*) CODESIGN_ARGS=("-p:CodesignKey=$hash"); break ;;
    esac
done < <(security find-identity -v -p codesigning | grep "Apple Development" | grep -o '[0-9A-F]\{40\}')
echo "Codesign: ${CODESIGN_ARGS[0]:-csproj default}"

# Build the JS bundle (npm ci + build:Debug), then the AppKit (net11.0-macos) app - the default Mac app.
# The TargetFrameworks override enables the opt-in macos TFM without pulling in android etc.
./npm-build.cmd || exit 1
dotnet build src/dotnet/App.Maui/ -f net11.0-macos '-p:TargetFrameworks="net11.0-macos;net11.0"' "${CODESIGN_ARGS[@]}" || exit 1

# The produced bundle name depends on IsDevMaui ("Voxt (Dev).app" for dev, "Voxt.app" for prod).
OUT_DIR="$REPO_ROOT/artifacts/bin/App.Maui/debug_net11.0-macos"
APP_PATH="$(ls -d "$OUT_DIR"/*.app 2>/dev/null | head -1)"
if [ -z "$APP_PATH" ]; then
    echo "error: no .app bundle found in $OUT_DIR" >&2
    exit 1
fi

# Terminate a previous instance, then run the binary directly so its logs stream to this terminal.
pkill -f "$APP_PATH/Contents/MacOS/" 2>/dev/null
echo "Launching: $APP_PATH"
exec "$APP_PATH/Contents/MacOS/ActualChat"
