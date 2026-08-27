#!/bin/sh
# Builds both targets into build/$CONFIG-$SDK/:
#   libVoxtActivityKitShim.a  - linked into the app via NativeReference
#   VoxtActivitiesWidget.appex - embedded via AdditionalAppExtensions
# Invoked from App.Maui.csproj (macOS + iOS only) and by hand during development.
# ../appex-build.sh holds the xcodegen and signing plumbing, and documents the APPEX_* vars.
set -e
cd "$(dirname "$0")"

APPEX_PROJECT=VoxtActivities
APPEX_CONFIG=${1:-Release}
APPEX_SDK=${2:-iphoneos}
APPEX_BUNDLE_ID=${3:-chat.actual.dev.app.widget}
# ${4-} / ${5-}, not ${4:-}: an empty argument must NOT fall back to the placeholder - see the
# ITMS-90473 check in ../appex-build.sh. The defaults are for hand runs that pass no version.
APPEX_SHORT_VERSION=${4-1.0}
APPEX_BUILD_VERSION=${5-1}
APPEX_TEAM=${6:-${VOXT_ACTIVITIES_TEAM:-}}
APPEX_IDENTITY=${7:-${VOXT_ACTIVITIES_IDENTITY:-Apple Development}}
APPEX_PROFILE=${8:-${VOXT_ACTIVITIES_PROFILE:-}}
APPEX_ENTITLEMENTS=${9:-${VOXT_ACTIVITIES_ENTITLEMENTS:-}}

. ../appex-build.sh

appex_generate_project
# A static archive is not a signable artifact - CodeSign fails outright on one - so the shim
# builds unsigned whatever the team. Only the widget takes the signing settings.
appex_build_target VoxtActivityKitShim unsigned
appex_build_target VoxtActivitiesWidget signed
