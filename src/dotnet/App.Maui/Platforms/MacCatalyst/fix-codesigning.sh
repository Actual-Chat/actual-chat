#!/bin/sh
# Workaround for a MAUI/iOS-SDK 26.1.10502 regression: the in-process Codesign task
# overwrites top-level .dylib files in the bundle's MonoBundle with the codesign
# argument string instead of signing them in place, and writes stray `.stampfile`
# entries at the root of each embedded .framework that then fail re-seal.
# This script restores any corrupted dylib from the linker-stage staging copy,
# deletes stray .stampfile files, and re-signs every dylib, framework, and the
# bundle itself.
# Args: $1 = APP bundle path  $2 = STAGING MonoBundle path  $3 = Entitlements.xcent path
#       $4 = signing identity (optional; "-" / empty => ad-hoc, for Debug)
# For Release/distribution pass the Apple Distribution identity so the nested code
# carries the same cert (App Store validation rejects ad-hoc nested code) and add a
# secure timestamp.
set -eu

APP=$1
STAGING=$2
ENT=$3
IDENTITY="${4:--}"
MONO="$APP/Contents/MonoBundle"
FW="$APP/Contents/Frameworks"

# Secure (network) timestamp only for distribution builds; ad-hoc and Apple
# Development signing don't need it and shouldn't depend on network access.
case "$IDENTITY" in
  ""|"-")            IDENTITY=-; TS="--timestamp=none" ;;
  *Distribution*)    TS="--timestamp" ;;
  *)                 TS="--timestamp=none" ;;
esac

# Resolve a generic identity name (e.g. "Apple Development") to a concrete SHA-1.
# Raw `codesign --sign` fails with "ambiguous" when several certs match the name
# (common locally: multiple Apple Development certs), even though the in-process
# SDK Codesign task tolerates it. A full unique name (the Distribution cert)
# resolves to itself, so this is a no-op there.
if [ "$IDENTITY" != "-" ]; then
  resolved=$(security find-identity -v -p codesigning | grep -F "$IDENTITY" | head -1 | awk '{print $2}')
  if [ -n "$resolved" ]; then
    echo "fix-codesigning: resolved '$IDENTITY' -> $resolved"
    IDENTITY="$resolved"
  fi
fi

echo "fix-codesigning: APP=$APP"
echo "fix-codesigning: STAGING=$STAGING"
echo "fix-codesigning: ENT=$ENT"
echo "fix-codesigning: IDENTITY=$IDENTITY"

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
    /usr/bin/codesign --force $TS --sign "$IDENTITY" "$f"
  done
fi

if [ -d "$FW" ]; then
  find "$FW" -name .stampfile -delete
  for fw in "$FW"/*.framework; do
    [ -d "$fw" ] || continue
    /usr/bin/codesign --force $TS --sign "$IDENTITY" "$fw"
  done
fi

if [ -f "$ENT" ]; then
  /usr/bin/codesign --force $TS --sign "$IDENTITY" --entitlements "$ENT" "$APP"
else
  /usr/bin/codesign --force $TS --sign "$IDENTITY" "$APP"
fi
