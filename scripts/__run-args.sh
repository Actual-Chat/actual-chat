#!/bin/bash

# Parses the arguments `b app build|run` forwards to the Apple run scripts:
#   --build-only  stop after the build - no device lookup, install or launch
#   --simulator   iOS: target a simulator instead of a connected device
#   --prod        the production app (IsDevMaui=false) instead of the dev one
#   --no-web      skip the npm web asset build
#   -- <args>     passed to dotnet build as is
# Sets IS_BUILD_ONLY, IS_SIMULATOR, MUST_SKIP_WEB, MAC_APP_NAME and BUILD_ARGS.
parse_run_args() {
    IS_BUILD_ONLY=""
    IS_SIMULATOR=""
    MUST_SKIP_WEB=""
    MAC_APP_NAME="Voxt (Dev).app"
    BUILD_ARGS=()
    while [ $# -gt 0 ]; do
        case "$1" in
            --build-only) IS_BUILD_ONLY=1 ;;
            --simulator) IS_SIMULATOR=1 ;;
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
