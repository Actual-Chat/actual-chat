#!/bin/bash
# Builds libopus.xcframework with the one slice OpusSharp.Natives doesn't ship:
# ios-arm64-maccatalyst (the app ships for maccatalyst-arm64 only). Mirrors the iOS job of OpusSharp's OpusCompile.yml
# (configure + make libopus.la, then opus_shim.c added to the archive), so OpusSharp.Core's
# static "__Internal" imports find the same symbols they find in the package's iOS slices.
# Run on a Mac with Xcode; needs nothing else. Env overrides: WORK, OUT, JOBS.
set -euo pipefail

OPUS_VERSION=1.6.1
OPUS_SHA256=6ffcb593207be92584df15b32466ed64bbec99109f007c82205f0194572411a1
# The commit OpusSharp.Natives 1.6.1.3 was built from (see its .nuspec)
OPUSSHARP_COMMIT=0147dd6e11b5e9750b683727a8242cb364c672b2
SHIM_SHA256=d8367068db8f299b9f95386cd45932785849681a274b9b4588ad0f90bd302a67
# App.Maui's SupportedOSPlatformVersion for -maccatalyst
MIN_VERSION=17.0

HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="${WORK:-$HERE/../../../../../../tmp/opus-maccatalyst}"
OUT="${OUT:-$HERE/libopus.xcframework}"
JOBS="${JOBS:-$(sysctl -n hw.ncpu)}"

mkdir -p "$WORK"
WORK="$(cd "$WORK" && pwd)"
cd "$WORK"

verify() {
  echo "$2  $1" | shasum -a 256 -c - >/dev/null
}

if [ ! -f "opus-$OPUS_VERSION.tar.gz" ] || ! verify "opus-$OPUS_VERSION.tar.gz" $OPUS_SHA256; then
  curl -sSfLO "https://downloads.xiph.org/releases/opus/opus-$OPUS_VERSION.tar.gz"
  verify "opus-$OPUS_VERSION.tar.gz" $OPUS_SHA256
fi
rm -rf "opus-$OPUS_VERSION" && tar xzf "opus-$OPUS_VERSION.tar.gz"
SRC="$WORK/opus-$OPUS_VERSION"

curl -sSfL -o opus_shim.c \
  "https://raw.githubusercontent.com/AvionBlock/OpusSharp/$OPUSSHARP_COMMIT/OpusSharp.Natives/opus_shim.c"
verify opus_shim.c $SHIM_SHA256

SDKROOT="$(xcrun --sdk macosx --show-sdk-path)"
CC="$(xcrun --sdk macosx --find clang)"
AR="$(xcrun --sdk macosx --find ar)"
RANLIB="$(xcrun --sdk macosx --find ranlib)"

FLAGS="-target arm64-apple-ios$MIN_VERSION-macabi -isysroot $SDKROOT -O3"
B="$WORK/build"
rm -rf "$B" && mkdir -p "$B" && cd "$B"

"$SRC/configure" \
  --host=aarch64-apple-darwin \
  --disable-shared \
  --enable-static \
  --disable-extra-programs \
  --disable-doc \
  CC="$CC" CFLAGS="$FLAGS" AR="$AR" RANLIB="$RANLIB" >configure.log
make -j "$JOBS" libopus.la >make.log

"$CC" $FLAGS -I"$SRC/include" -c "$WORK/opus_shim.c" -o opus_shim.o
# One prelinked object instead of OpusSharp's llvm-objcopy renaming: everything but the
# opus_* API and the opussharp_* shim becomes private extern, so celt_* / silk_* can't
# clash with another libopus linked into the same binary.
printf '_opus_*\n_opussharp_*\n' >exported.txt
"$CC" $FLAGS -r -nostdlib -Wl,-exported_symbols_list,exported.txt \
  -Wl,-force_load,.libs/libopus.a opus_shim.o -o libopus.o
rm -f libopus.a
"$AR" rcs libopus.a libopus.o
"$RANLIB" libopus.a
cd "$WORK"

rm -rf "$WORK/include" && mkdir "$WORK/include" && cp "$SRC"/include/*.h "$WORK/include/"
rm -rf "$OUT"
xcodebuild -create-xcframework \
  -library "$B/libopus.a" -headers "$WORK/include" \
  -output "$OUT" >/dev/null

lipo -info "$OUT"/*/libopus.a
