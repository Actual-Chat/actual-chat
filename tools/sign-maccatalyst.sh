#!/usr/bin/env bash
# Wraps a Release-signed Mac Catalyst .app into an App-Store-uploadable .pkg.
#
# Usage:
#   tools/sign-maccatalyst.sh dev   # IsDevMaui=true  -> chat.actual.dev.app
#   tools/sign-maccatalyst.sh prod  # IsDevMaui=false -> chat.actual.app
#
# Prerequisites:
#   - .app already built with: dotnet publish src/dotnet/App.Maui/App.Maui.csproj
#       -f net10.0-maccatalyst -c Release -p:IsDevMaui=<true|false>
#   - Installed in Keychain: "3rd Party Mac Developer Installer: Actual Chat Inc. (M287G8G83F)"
#
# Output:
#   artifacts/maccatalyst/<bundle-id>-<version>.pkg

set -euo pipefail

variant="${1:-}"
case "$variant" in
  dev)  app_name="Voxt (Dev).app"; bundle_id="chat.actual.dev.app" ;;
  prod) app_name="Voxt.app";       bundle_id="chat.actual.app" ;;
  *) echo "Usage: $0 {dev|prod}" >&2; exit 2 ;;
esac

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Prefer the publish output (what CI ships), fall back to the plain build output
# (what a local `dotnet build` produces) and finally the codesign staging dir.
app_path=""
for candidate in \
  "$repo_root/artifacts/publish/App.Maui/release_net10.0-maccatalyst_maccatalyst-arm64/$app_name" \
  "$repo_root/artifacts/bin/App.Maui/release_net10.0-maccatalyst_maccatalyst-arm64/$app_name" \
  "$repo_root/artifacts/out/codesign/$app_name"; do
  if [ -d "$candidate" ]; then app_path="$candidate"; break; fi
done
if [ -z "$app_path" ]; then
  echo "Could not find .app for variant=$variant. Build it first with:" >&2
  echo "  dotnet publish src/dotnet/App.Maui/App.Maui.csproj -f net10.0-maccatalyst -c Release -p:IsDevMaui=$( [ "$variant" = "dev" ] && echo true || echo false )" >&2
  exit 1
fi

installer_cert="3rd Party Mac Developer Installer: Actual Chat Inc. (M287G8G83F)"
if ! security find-identity -v -p basic 2>/dev/null | grep -F "$installer_cert" >/dev/null; then
  echo "Missing installer cert: $installer_cert" >&2
  echo "Create one in Apple Developer Console (Mac Installer Distribution), download, and double-click to install." >&2
  exit 1
fi

out_dir="$repo_root/artifacts/maccatalyst"
mkdir -p "$out_dir"
version="$(/usr/libexec/PlistBuddy -c 'Print CFBundleShortVersionString' "$app_path/Contents/Info.plist" 2>/dev/null || echo unknown)"
pkg_path="$out_dir/${bundle_id}-${version}.pkg"

echo "Signing  : $app_path"
echo "Wrapping : $pkg_path"
productbuild \
  --component "$app_path" /Applications \
  --sign "$installer_cert" \
  "$pkg_path"

echo
echo "Done. Upload to App Store Connect with Transporter or:"
echo "  xcrun altool --upload-app -f \"$pkg_path\" -t macos -u <apple-id> -p <app-specific-password>"
