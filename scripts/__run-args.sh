#!/bin/bash

# Parses the arguments `b app build|run` forwards to the Apple run scripts:
#   --build-only  stop after the build - no device lookup, install or launch
#   --no-launch   iOS: stop after the install
#   --simulator   iOS: target a simulator instead of a connected device
#   --catalyst    macOS: the Mac Catalyst app instead of the AppKit one
#   --prod        the production app (IsDevMaui=false) instead of the dev one
#   --no-web      skip the npm web asset build
#   -- <args>     passed to dotnet build as is
# Sets IS_BUILD_ONLY, MUST_SKIP_LAUNCH, IS_SIMULATOR, IS_CATALYST, MUST_SKIP_WEB, MAC_APP_NAME and BUILD_ARGS.
parse_run_args() {
    IS_BUILD_ONLY=""
    MUST_SKIP_LAUNCH=""
    IS_SIMULATOR=""
    IS_CATALYST=""
    MUST_SKIP_WEB=""
    MAC_APP_NAME="Voxt (Dev).app"
    BUILD_ARGS=()
    while [ $# -gt 0 ]; do
        case "$1" in
            --build-only) IS_BUILD_ONLY=1 ;;
            --no-launch) MUST_SKIP_LAUNCH=1 ;;
            --simulator) IS_SIMULATOR=1 ;;
            --catalyst) IS_CATALYST=1 ;;
            --no-web) MUST_SKIP_WEB=1 ;;
            --prod)
                MAC_APP_NAME="Voxt.app"
                BUILD_ARGS+=("-p:IsDevMaui=false")
                ;;
            --)
                shift
                BUILD_ARGS+=("$@")
                break
                ;;
            *)
                echo "error: unknown argument: $1" >&2
                exit 1
                ;;
        esac
        shift
    done
}
