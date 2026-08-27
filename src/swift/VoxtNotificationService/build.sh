#!/bin/sh
# Builds VoxtNotificationService.appex into build/$CONFIG-$SDK/ - embedded into the app via
# AdditionalAppExtensions from App.Maui.csproj (macOS + iOS only) and buildable by hand.
# ../appex-build.sh holds the xcodegen and signing plumbing, and documents the APPEX_* vars.
set -e
cd "$(dirname "$0")"

APPEX_PROJECT=VoxtNotificationService
APPEX_CONFIG=${1:-Release}
APPEX_SDK=${2:-iphoneos}
APPEX_BUNDLE_ID=${3:-chat.actual.dev.app.notification}
# ${4-} / ${5-}, not ${4:-}: an empty argument must NOT fall back to the placeholder - see the
# ITMS-90473 check in ../appex-build.sh. The defaults are for hand runs that pass no version.
APPEX_SHORT_VERSION=${4-1.0}
APPEX_BUILD_VERSION=${5-1}
APPEX_TEAM=${6:-${VOXT_NOTIFICATION_SERVICE_TEAM:-}}
APPEX_IDENTITY=${7:-${VOXT_NOTIFICATION_SERVICE_IDENTITY:-Apple Development}}
APPEX_PROFILE=${8:-${VOXT_NOTIFICATION_SERVICE_PROFILE:-}}
APPEX_ENTITLEMENTS=${9:-${VOXT_NOTIFICATION_SERVICE_ENTITLEMENTS:-}}

. ../appex-build.sh

appex_generate_project
appex_build_target VoxtNotificationService signed
