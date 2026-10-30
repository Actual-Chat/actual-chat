# libopus for Mac Catalyst

`OpusSharp.Natives` ships libopus for every platform the app runs on except Mac Catalyst: its
`libopus.xcframework` has only the `ios-arm64` and `ios-arm64_x86_64-simulator` slices, and its
`buildTransitive` targets wire it into `-ios` only. Its `osx-*` dylibs are built for the macOS
platform, which the linker refuses in a Catalyst binary.

`libopus.xcframework` here is the missing `ios-arm64-maccatalyst` slice — arm64 only, since the app
ships for `maccatalyst-arm64` only. App.Maui links it statically on `-maccatalyst`, and
OpusSharp.Core reaches it through the same `__Internal` imports it uses on iOS.

It's built from the same sources as the package's iOS slices — the libopus 1.6.1 release and
OpusSharp's `opus_shim.c` at the commit `OpusSharp.Natives` 1.6.1.3 was built from — with the
same `configure` flags. There is one difference: instead of renaming the internal symbols with
`llvm-objcopy`, the script prelinks everything into one object that exports only `opus_*` and
`opussharp_*`.

## Rebuilding

```bash
src/dotnet/App.Maui/Platforms/MacCatalyst/Opus/build-maccatalyst.sh
```

Needs only Xcode. Takes about 30 seconds; intermediate files go to `tmp/opus-maccatalyst`.
When `OpusSharp.Natives` is bumped, update `OPUS_VERSION`, `OPUSSHARP_COMMIT` and both
checksums in the script, then rebuild and commit the new `libopus.xcframework`.

It lives under `Platforms/MacCatalyst` so that dropping Mac Catalyst removes it too; the only other
reference is the `NativeReference` in App.Maui.csproj's `-maccatalyst` item group. Delete it earlier if
`OpusSharp.Natives` starts shipping a `maccatalyst` slice itself.
