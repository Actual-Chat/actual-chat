#!/bin/zsh
# Records the iOS startup profile on the Mac. Run it there: /mibc-update drives it over ssh.
#
#   [BRANCH=dev] [EXPECT_SHA=<sha>] [CLEAN=1] record-ios-startup-profile.sh build
#                                                  sync to origin/$BRANCH (fails unless it is EXPECT_SHA), build Release with the diagnostic port
#   record-ios-startup-profile.sh install          install the build on the iPhone
#   record-ios-startup-profile.sh record [HH:MM:SS]  launch suspended, trace while someone drives the app
#   record-ios-startup-profile.sh convert          .nettrace -> ios.mibc
#
# The trace lists the methods that ran interpreted on the build it was recorded on. The result is
# only the new recording; whether to merge the old ios.mibc into it is decided afterwards, see
# /mibc-update. See docs/ios-specific.md -> Measuring what runs interpreted.

set -e
export DEVELOPER_DIR=${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

UDID=${IOS_UDID:-00008110-000405C426F1801E}
BUNDLE_ID=chat.actual.dev.app
BRANCH=${BRANCH:-dev}
ROOT=$HOME/Projects/ActualChat
APP=$ROOT/artifacts/bin/App.Maui/release_net11.0-ios_ios-arm64/ActualChat.app
PROFILING=$ROOT/src/dotnet/App.Maui/_Profiling
WORK=$ROOT/tmp/ios-profile
mkdir -p "$WORK"
cd "$ROOT"

kill_app() {
    # Prod Voxt is on the phone too and is also named ActualChat.app: match on this bundle's container.
    xcrun devicectl device info apps --device $UDID --bundle-id $BUNDLE_ID --include-all-apps \
        --json-output "$WORK/app.json" > /dev/null 2>&1 || return 0
    local container=$(python3 -c 'import json,re,sys
apps=json.load(open(sys.argv[1]))["result"]["apps"]
m=re.search(r"[0-9A-Fa-f]{8}-[0-9A-Fa-f-]{27}", apps[0]["url"]) if apps else None
print(m.group(0) if m else "")' "$WORK/app.json")
    [ -n "$container" ] || return 0
    local pid
    for pid in $(xcrun devicectl device info processes --device $UDID 2>/dev/null | grep "$container" | awk '{print $1}'); do
        xcrun devicectl device process signal --device $UDID --signal SIGKILL --pid $pid > /dev/null 2>&1 || true
    done
    sleep 2
}

build() {
    local backup=/tmp/ios-profile-firebase
    local files=(Platforms/iOS/GoogleService-Info.plist.dev Platforms/iOS/GoogleService-Info.plist.prod \
                 Platforms/Android/Resources/google-services.json.dev Platforms/Android/Resources/google-services.json.prod)
    mkdir -p $backup
    local f
    for f in $files; do cp "src/dotnet/App.Maui/$f" "$backup/$(basename $f)"; done

    echo "=== sync to origin/$BRANCH"
    git checkout -- . 2>/dev/null || true
    git fetch origin $BRANCH
    git checkout $BRANCH
    git reset --hard origin/$BRANCH
    git log --oneline -1
    if [ -n "$EXPECT_SHA" ] && [ "$(git rev-parse HEAD)" != "$EXPECT_SHA" ]; then
        echo "!!! origin/$BRANCH is at $(git rev-parse HEAD), expected $EXPECT_SHA - push first"
        exit 1
    fi

    # The reset restores the skip-worktree Firebase placeholders; an app built with them aborts at launch.
    for f in $files; do
        [ $(wc -c < "src/dotnet/App.Maui/$f") -lt 200 ] && cp "$backup/$(basename $f)" "src/dotnet/App.Maui/$f"
    done

    # A runtime bump needs a clean rebuild: the dylibtoframework cache is not invalidated by it.
    if [ -n "$CLEAN" ]; then
        rm -rf artifacts/out artifacts/obj/App.Maui artifacts/obj/App.Maui.IosShareExt \
               artifacts/bin/App.Maui/release_net11.0-ios_ios-arm64
    fi

    npm install
    npm run build:Release
    security unlock-keychain -p "$(cat ~/.ac-signing/keychain-password)" ~/Library/Keychains/ac-signing.keychain-db

    echo "=== build: Release / net11.0-ios / ios-arm64, diagnostic port on and suspended"
    (cd src/dotnet/App.Maui && dotnet build -noLogo -maxCpuCount -nodeReuse:false \
        -f net11.0-ios -p:RuntimeIdentifier=ios-arm64 -c Release -p:IsDevMaui=true \
        -p:ArchiveOnBuild=false \
        -p:EnableDiagnostics=true -p:DiagnosticAddress=127.0.0.1 -p:DiagnosticPort=9000 \
        -p:DiagnosticListenMode=listen -p:DiagnosticSuspend=true \
        -p:CustomAfterMicrosoftCommonTargets=$ROOT/tmp/dev-signing.targets)

    grep -q 'DOTNET_DiagnosticPorts' artifacts/out/linker-cache/main.arm64.mm \
        || { echo "!!! the diagnostic port is not in the bundle"; exit 1; }
    echo "=== BUILD OK"
}

install() {
    kill_app
    xcrun devicectl device install app --device $UDID "$APP"
}

record() {
    local duration=${1:-00:02:00}
    rm -f "$WORK/ios.nettrace"
    pkill -f dotnet-dsrouter || true
    pkill -f dotnet-trace || true
    sleep 1
    kill_app

    # Launch first: the app suspends and holds :9000; dsrouter dials it the moment a tool attaches.
    echo "=== launch (suspends)"
    xcrun devicectl device process launch --device $UDID $BUNDLE_ID
    sleep 6
    (dotnet-dsrouter ios -v debug > "$WORK/dsrouter.log" 2>&1 &)
    sleep 4
    local dsr=$(pgrep -f "dotnet-dsrouter ios" | head -1)
    echo "=== TRACING for $duration - the app is live, use it now"
    date "+start %H:%M:%S"
    dotnet-trace collect -p $dsr \
        --providers "Microsoft-Windows-DotNETRuntime:0x1C000080018:5" \
        --buffersize 512 --duration $duration -o "$WORK/ios.nettrace"
    date "+end   %H:%M:%S"
    pkill -f dotnet-dsrouter || true
    ls -la "$WORK/ios.nettrace"
}

convert() {
    dotnet tools/dotnet-pgo/dotnet-pgo.dll create-mibc --trace "$WORK/ios.nettrace" --output "$WORK/ios.mibc" \
        --compressed --reference "artifacts/out/R2R/*.dll"
    dotnet tools/dotnet-pgo/dotnet-pgo.dll dump -i "$WORK/ios.mibc" -o "$WORK/dump.txt" | grep '# Methods:'
    echo "=== result: $WORK/ios.mibc"
}

case "$1" in
    build) build ;;
    install) install ;;
    record) record "$2" ;;
    convert) convert ;;
    *) echo "usage: $0 build|install|record [HH:MM:SS]|convert"; exit 2 ;;
esac
