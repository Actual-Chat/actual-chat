#!/bin/sh
# Shared xcodegen + signing plumbing for the Xcode projects under src/swift/.
# Sourced - never executed - by each project's build.sh, which first sets:
#
#   APPEX_PROJECT          <name>.xcodeproj, generated next to the sourcing script
#   APPEX_CONFIG           Debug | Release
#   APPEX_SDK              iphoneos | iphonesimulator
#   APPEX_BUNDLE_ID        overrides PRODUCT_BUNDLE_IDENTIFIER (the app is flavor-conditional)
#   APPEX_SHORT_VERSION    MARKETING_VERSION
#   APPEX_BUILD_VERSION    CURRENT_PROJECT_VERSION
#   APPEX_TEAM             empty = build unsigned, which is all CI and the simulator need
#   APPEX_IDENTITY         "Apple Development" locally, "Apple Distribution" for TestFlight
#   APPEX_PROFILE          naming one switches Xcode from automatic to manual signing
#   APPEX_ENTITLEMENTS     relative to the project dir; only used on the signed path
#
# and then calls appex_generate_project once, followed by appex_build_target per target.

# An empty version is a hard error rather than something to paper over with a default: App
# Store Connect rejects an upload whose extension's CFBundleShortVersionString /
# CFBundleVersion disagree with the host app (ITMS-90473), and a build.sh can be invoked
# before NBGV has filled the version properties in.
if [ -z "$APPEX_SHORT_VERSION" ] || [ -z "$APPEX_BUILD_VERSION" ]; then
    echo "$APPEX_PROJECT/build.sh: empty version (short='$APPEX_SHORT_VERSION', build='$APPEX_BUILD_VERSION')." >&2
    echo "The .appex must carry the app's versions - see ITMS-90473." >&2
    exit 1
fi

appex_generate_project() {
    if [ -d "$APPEX_PROJECT.xcodeproj" ] && [ ! project.yml -nt "$APPEX_PROJECT.xcodeproj/project.pbxproj" ]; then
        return
    fi
    if ! command -v xcodegen >/dev/null 2>&1; then
        echo "$APPEX_PROJECT.xcodeproj is missing or stale and xcodegen isn't installed." >&2
        echo "Run 'brew install xcodegen', or regenerate it on a Mac that has it." >&2
        exit 1
    fi
    xcodegen generate
}

# appex_build_target <target> signed|unsigned
#
# The .NET iOS SDK re-signs the .appex it embeds, but it never gives it an
# embedded.mobileprovision - only the app bundle gets one - so a device or TestFlight build
# needs Xcode to sign the appex here, which takes a team id.
#
# CODE_SIGN_IDENTITY is load-bearing: every project.yml pins it to "" for the unsigned
# default, an empty identity means "skip CodeSign entirely", and a project-level setting
# outranks automatic signing - so without overriding it on the command line a signed build
# reports success and silently ships an unsigned appex. CODE_SIGNING_REQUIRED=YES then turns
# any future silent skip into a hard failure.
#
# Automatic signing only ever picks a development identity: combining it with "Apple
# Distribution" makes Xcode fail with "conflicting provisioning settings". So distribution
# builds must go manual, which in turn requires naming the profile explicitly - hence
# APPEX_PROFILE selecting between the two modes rather than a second identity knob.
appex_build_target() {
    _target=$1
    _mode=$2

    if [ "$_mode" != signed ] || [ -z "$APPEX_TEAM" ]; then
        set -- CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO CODE_SIGN_IDENTITY=
    elif [ -n "$APPEX_PROFILE" ]; then
        set -- CODE_SIGN_STYLE=Manual "DEVELOPMENT_TEAM=$APPEX_TEAM" \
            "CODE_SIGN_IDENTITY=$APPEX_IDENTITY" "PROVISIONING_PROFILE_SPECIFIER=$APPEX_PROFILE" \
            CODE_SIGNING_ALLOWED=YES CODE_SIGNING_REQUIRED=YES
    else
        set -- CODE_SIGN_STYLE=Automatic "DEVELOPMENT_TEAM=$APPEX_TEAM" \
            "CODE_SIGN_IDENTITY=$APPEX_IDENTITY" CODE_SIGNING_ALLOWED=YES CODE_SIGNING_REQUIRED=YES \
            -allowProvisioningUpdates
    fi
    # Xcode derives an entitlements file from the profile on its own, but CI re-signs the
    # embedded appex afterwards and must use the same file - so the signed path names it.
    if [ "$_mode" = signed ] && [ -n "$APPEX_TEAM" ] && [ -n "$APPEX_ENTITLEMENTS" ]; then
        set -- "$@" "CODE_SIGN_ENTITLEMENTS=$APPEX_ENTITLEMENTS"
    fi

    xcodebuild -project "$APPEX_PROJECT.xcodeproj" -target "$_target" \
        -configuration "$APPEX_CONFIG" -sdk "$APPEX_SDK" \
        SYMROOT=build OBJROOT=build/obj \
        PRODUCT_BUNDLE_IDENTIFIER="$APPEX_BUNDLE_ID" \
        MARKETING_VERSION="$APPEX_SHORT_VERSION" \
        CURRENT_PROJECT_VERSION="$APPEX_BUILD_VERSION" \
        "$@" \
        build
}
