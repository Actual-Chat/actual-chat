#!/bin/sh
# Builds both targets into build/$CONFIG-$SDK/:
#   libVoxtActivityKitShim.a  - linked into the app via NativeReference
#   VoxtActivitiesWidget.appex - embedded via AdditionalAppExtensions
# Invoked from App.Maui.csproj (macOS + iOS only) and by hand during development.
set -e
cd "$(dirname "$0")"

CONFIG=${1:-Release}
SDK=${2:-iphoneos}
BUNDLE_ID=${3:-chat.actual.dev.app.widget}
# ${4-} / ${5-}, not ${4:-}: an empty argument must NOT fall back to the placeholder, or a
# caller whose version properties aren't computed yet silently ships an appex whose
# CFBundleShortVersionString / CFBundleVersion don't match the app - which is an App Store
# Connect ITMS-90473 rejection. The defaults are for hand runs that pass no version at all.
SHORT_VERSION=${4-1.0}
BUILD_VERSION=${5-1}
DEV_TEAM=${6:-${VOXT_ACTIVITIES_TEAM:-}}
# "Apple Development" for local device builds; pass "Apple Distribution" for TestFlight.
IDENTITY=${7:-${VOXT_ACTIVITIES_IDENTITY:-Apple Development}}
# A profile name switches signing from automatic to manual - see the comment below.
PROFILE=${8:-${VOXT_ACTIVITIES_PROFILE:-}}
# Relative to this folder. Xcode derives one from the profile when it's omitted, but CI
# re-signs the embedded appex afterwards and needs the same file to sign it with.
ENTITLEMENTS=${9:-${VOXT_ACTIVITIES_ENTITLEMENTS:-}}

if [ -z "$SHORT_VERSION" ] || [ -z "$BUILD_VERSION" ]; then
    echo "build.sh: empty version (short='$SHORT_VERSION', build='$BUILD_VERSION')." >&2
    echo "The .appex must carry the app's versions - see ITMS-90473." >&2
    exit 1
fi

if [ ! -d VoxtActivities.xcodeproj ] || [ project.yml -nt VoxtActivities.xcodeproj/project.pbxproj ]; then
    if ! command -v xcodegen >/dev/null 2>&1; then
        echo "VoxtActivities.xcodeproj is missing or stale and xcodegen isn't installed." >&2
        echo "Run 'brew install xcodegen', or regenerate it on a Mac that has it." >&2
        exit 1
    fi
    xcodegen generate
fi

# The .NET SDK re-signs the .appex it embeds, but it never gives it an
# embedded.mobileprovision - only the app bundle gets one. So a device or TestFlight build
# needs Xcode to sign the appex here, which takes a team id. Without one we build unsigned,
# which is all the simulator and a headless CI compile need.
build_target() {
    TARGET=$1
    shift
    xcodebuild -project VoxtActivities.xcodeproj -target "$TARGET" \
        -configuration "$CONFIG" -sdk "$SDK" \
        SYMROOT=build OBJROOT=build/obj \
        PRODUCT_BUNDLE_IDENTIFIER="$BUNDLE_ID" \
        MARKETING_VERSION="$SHORT_VERSION" \
        CURRENT_PROJECT_VERSION="$BUILD_VERSION" \
        "$@" \
        build
}

# CODE_SIGN_IDENTITY is load-bearing: project.yml pins it to "" for the unsigned default, an
# empty identity means "skip CodeSign entirely", and a project-level setting outranks automatic
# signing - so without this override a signed build reports success and silently ships an
# unsigned appex. Command-line settings beat project-level ones, which is what makes it work.
# CODE_SIGNING_REQUIRED=YES then turns any future silent skip into a hard failure.
# Automatic signing only ever picks a development identity: combining it with "Apple
# Distribution" makes Xcode fail with "conflicting provisioning settings". So distribution
# builds must go manual, which in turn requires naming the profile explicitly - hence PROFILE
# selecting between the two modes rather than a second identity knob.
if [ -n "$DEV_TEAM" ] && [ -n "$PROFILE" ]; then
    set -- CODE_SIGN_STYLE=Manual "DEVELOPMENT_TEAM=$DEV_TEAM" \
        "CODE_SIGN_IDENTITY=$IDENTITY" "PROVISIONING_PROFILE_SPECIFIER=$PROFILE" \
        CODE_SIGNING_ALLOWED=YES CODE_SIGNING_REQUIRED=YES
elif [ -n "$DEV_TEAM" ]; then
    set -- CODE_SIGN_STYLE=Automatic "DEVELOPMENT_TEAM=$DEV_TEAM" \
        "CODE_SIGN_IDENTITY=$IDENTITY" CODE_SIGNING_ALLOWED=YES CODE_SIGNING_REQUIRED=YES \
        -allowProvisioningUpdates
else
    set -- CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO CODE_SIGN_IDENTITY=
fi

if [ -n "$DEV_TEAM" ] && [ -n "$ENTITLEMENTS" ]; then
    set -- "$@" "CODE_SIGN_ENTITLEMENTS=$ENTITLEMENTS"
fi

# A static archive is not a signable artifact - CodeSign fails outright on one - so the shim
# builds unsigned whatever the team. Only the widget takes the signing settings.
build_target VoxtActivityKitShim CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO CODE_SIGN_IDENTITY=
build_target VoxtActivitiesWidget "$@"
