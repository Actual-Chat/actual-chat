#!/bin/sh
# Workaround for a MAUI/iOS-SDK 26.1.10502 regression: the in-process Codesign task
# overwrites top-level .dylib files in the bundle's MonoBundle with the codesign
# argument string instead of signing them in place, and writes stray `.stampfile`
# entries at the root of each embedded .framework that then fail re-seal.
# This script restores any corrupted dylib from the linker-stage staging copy,
# deletes stray .stampfile files, and ad-hoc re-signs every dylib, framework, and
# the bundle itself.
# Args: $1 = APP bundle path  $2 = STAGING MonoBundle path  $3 = Entitlements.xcent path
set -eu

APP=$1
STAGING=$2
ENT=$3
MONO="$APP/Contents/MonoBundle"
FW="$APP/Contents/Frameworks"

echo "fix-codesigning: APP=$APP"
echo "fix-codesigning: STAGING=$STAGING"
echo "fix-codesigning: ENT=$ENT"

if [ -d "$MONO" ]; then
  for f in "$MONO"/*.dylib; do
    [ -f "$f" ] || continue
    name=$(basename "$f")
    if /usr/bin/file -b "$f" | grep -q Mach-O; then
      echo "fix-codesigning: $name already Mach-O, re-signing"
    else
      src="$STAGING/$name"
      if [ -f "$src" ]; then
        echo "fix-codesigning: restoring $name from staging"
        cp -f "$src" "$f"
      else
        echo "fix-codesigning: WARNING: no staging copy for $name at $src"
      fi
    fi
    /usr/bin/codesign --force --timestamp=none --sign - "$f"
  done
fi

if [ -d "$FW" ]; then
  find "$FW" -name .stampfile -delete
  for fw in "$FW"/*.framework; do
    [ -d "$fw" ] || continue
    /usr/bin/codesign --force --timestamp=none --sign - "$fw"
  done
fi

if [ -f "$ENT" ]; then
  /usr/bin/codesign --force --timestamp=none --sign - --entitlements "$ENT" "$APP"
else
  /usr/bin/codesign --force --timestamp=none --sign - "$APP"
fi
