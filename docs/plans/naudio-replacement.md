# NAudio replacement on Windows (NativeAOT)

## Problem

Under NativeAOT publish (`PublishAot=true` + `TrimMode=full`), NAudio's WASAPI code
path throws at runtime:

```
System.InvalidProgramException: Common Language Runtime detected an invalid program.
  The body of method 'Void NAudio.CoreAudioApi.Interfaces.MMDeviceEnumeratorComObject..ctor()' is invalid.
   at Internal.Runtime.TypeLoaderExceptionHelper.CreateInvalidProgramException
   at NAudio.CoreAudioApi.Interfaces.MMDeviceEnumeratorComObject..ctor() + 0x15
   at NAudio.CoreAudioApi.MMDeviceEnumerator..ctor() + 0x2b
   at ActualChat.App.Maui.Audio.WindowsAudioCapture.<Capture>d__6.MoveNext()
```

ILC emits an invalid body for NAudio's `ComObject`-derived wrapper classes.

### Why this can't be fixed via trim hints

- `[ComImport]`-style COM interop is not AOT-compatible. Tried:
  `TrimmerRootAssembly Include="NAudio.Wasapi" RootMode="All"`, `NAudio.Core` —
  keeps types/metadata but does not affect the miscompiled ctor body.
  `BuiltInComInteropSupport=true` also does not resolve it.
- Confirmed known limitation:
  - [naudio/NAudio#1211 — "InvalidProgramException under NativeAOT"](https://github.com/naudio/NAudio/issues/1211)
  - [dotnet/runtime#75060 — "[NativeAOT] Avalonia COM Interop is not supported"](https://github.com/dotnet/runtime/issues/75060)
  - [dotnet/runtimelab#306 — COM interop support](https://github.com/dotnet/runtimelab/issues/306)
  - [dotnet/maui#31227 — Windows NativeAOT improvements](https://github.com/dotnet/maui/issues/31227)
  - NAudio's own AOT discussion: [naudio/NAudio#1103](https://github.com/naudio/NAudio/discussions/1103)
- NAOT's documented workaround is to **migrate `[ComImport]` to the
  `ComWrappers` source-generator pattern** (`[GeneratedComInterface]` +
  `"allowMarshaling": false`). NAudio hasn't done that work and has no open PR.

## What we use from NAudio today

Location: `src/dotnet/App.Maui/Platforms/Windows/Audio/`

```
WindowsAudioCapture.cs
CustomWasapiLoopbackCapture.cs
```

| # | API | Purpose |
|---|---|---|
| 1 | `MMDeviceEnumerator` + `GetDefaultAudioEndpoint(DataFlow, Role)` | Resolve default capture (mic) and render (for loopback) devices |
| 2 | `WasapiCapture` (subclassed into `CustomWasapiLoopbackCapture`) with `AudioClientStreamFlags.Loopback`, `DataAvailable`, `StartRecording`, `StopRecording` | Capture **system audio** (what's playing out of the render endpoint) to feed as the reverse stream to our APM (Acoustic Echo Cancellation) |
| 3 | `MMDevice.AudioEndpointVolume` → `MasterVolumeLevelScalar`, `OnVolumeNotification` | Read + write mic volume, and subscribe to OS-level volume-change notifications to keep APM's analog-gain loop in sync |
| 4 | `WaveFormat.CreateIeeeFloatWaveFormat`, `WaveFormatEncoding`, `WaveInEventArgs` | Format descriptors / event payload for #2 |

The microphone **capture** itself does not use NAudio — it goes through
WinRT `Windows.Media.Audio.AudioGraph` (see `AudioGraph.CreateDeviceInputNodeAsync`
+ `FrameOutputNode`), which IS AOT-safe via CsWinRT.

Loss under the current graceful-degradation path (if NAudio ctors throw):
- AEC reverse stream is fed silent frames ⇒ AEC cannot remove echoes from
  speaker output.
- Mic analog-gain control does not follow APM's recommendations; the mic
  volume stays at whatever the user/OS last set it to.

## Candidate replacements

| # | Option | Covers 1 / 2 / 3 / 4 | NAOT-safe | Effort | Maturity |
|---|---|---|---|---|---|
| 1 | [CsWin32](https://github.com/microsoft/CsWin32) with `NativeMethods.json` `"allowMarshaling": false` + `<CsWin32RunAsBuildTask>true</CsWin32RunAsBuildTask>` ([AOT discussion #1169](https://github.com/microsoft/CsWin32/discussions/1169)) | ✅ all | ✅ ComWrappers SG | Spec ~8 WASAPI interfaces in `NativeMethods.txt`; port the ~40 lines of call sites. ~1 day | MS-owned, active |
| 2 | [DirectNAot](https://github.com/smourier/DirectNAot) (fork of DirectN, explicitly AOT-friendly, ComWrappers + `LibraryImport`, .NET 9+) | ✅ all (lists WASAPI + CoreAudio) | ✅ ComWrappers SG | Swap `using NAudio.CoreAudioApi` → `DirectNAot.Media.Audio.CoreAudio`; rename a handful of types. ~½ day | Community, actively maintained |
| 3 | Pure WinRT `AudioGraph` (already our mic path) | 1 ✅, 3 partial (`AudioDeviceInputNode.OutgoingGain`), **2 ✗ no system loopback** | ✅ CsWinRT | Lose AEC reverse-stream entirely (≈ current graceful-degradation state) | — |
| 4 | `Windows.Media.Capture` + `AudioPlaybackCaptureSettings` ([sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/)) | Per-process loopback only — does not capture the system render mix | ✅ CsWinRT | Doesn't fit AEC reverse-stream use case | — |
| 5 | **Disable native capture on Windows AOT; fall back to WebView JS (`getUserMedia`) like the pure-web build** | 1 ✅ (browser-resolved), **2 ✗** (browser can't do system loopback either), 3 ✗ | ✅ no native dep | Skip registering `IAudioCapture` on Windows-AOT; let the existing Blazor/JS recording path carry the workload. Minutes. | — |
| 6 | Wait for NAudio's own `ComWrappers` migration ([discussion #1103](https://github.com/naudio/NAudio/discussions/1103)) | — | — | No timeline / no PR | No PR in sight |
| 7 | Hand-rolled P/Invoke + manual `[GeneratedComInterface]` | ✅ all | ✅ | Tedious — ~8 interfaces × manual marshalling | — |

## Feature gap matrix

| Feature | CsWin32 | DirectNAot | WinRT only | WebView JS fallback |
|---|---|---|---|---|
| Default capture/render device | ✅ | ✅ | ✅ | ✅ (`getUserMedia` selects default; no app-side device pick) |
| System loopback capture (AEC reverse stream) | ✅ | ✅ | ✗ | ✗ (no browser API for system-render loopback) |
| Mic volume get/set/notification | ✅ | ✅ | partial (input-node gain, not OS mic volume) | ✗ (browser owns input gain) |
| WaveFormat helpers | trivial to reimpl | ✅ | ✅ | n/a |

System loopback capture is the hard feature — it does not exist in WinRT
(`AudioGraph`) nor in the browser. Only WASAPI offers it, and only
ComWrappers-based access survives NativeAOT.

## Recommendation

**Pick CsWin32 or DirectNAot.** Both remove the `InvalidProgramException`
entirely and also recover AEC loopback + mic volume control, which the current
graceful-degradation fallback sacrifices.

- **DirectNAot** — lowest effort: swap namespaces, rename a handful of types.
  Kicks the dependency from `NAudio` to `DirectNAot` in `App.Maui.csproj`.
  Surface area closest to NAudio's `CoreAudioApi` shape.
- **CsWin32** — most surgical, "blessed" by Microsoft. Only the ~8 WASAPI
  interfaces we actually use end up in the binary. Slightly more setup
  (`NativeMethods.txt` + `NativeMethods.json` `"allowMarshaling": false` +
  `<CsWin32RunAsBuildTask>true</CsWin32RunAsBuildTask>`). Best long-term.

Not recommended:

- **Pure WinRT / JS fallback** — loses AEC loopback. Worth it only if we
  accept degraded echo-cancellation quality on Windows as a permanent
  trade-off.
- **Waiting for NAudio** — no movement upstream; not a plan.

## Plan of record (to be confirmed)

1. Land the graceful-degradation fix that's already in `WindowsAudioCapture.cs`
   as the safety net — a) the path must not crash; b) mic-only recording still
   works.
2. Spike one of CsWin32 / DirectNAot in a branch, rewrite
   `WindowsAudioCapture.cs` against it, publish an NAOT build, verify:
   - `MMDeviceEnumerator`-style default-device lookup works.
   - Loopback capture produces non-silent reverse-stream samples when audio
     plays out of the default render device.
   - Mic volume notification + set still works.
3. Delete the `<TrimmerRootAssembly Include="NAudio.*">` entries and the
   graceful-degradation branch once the rewrite is proven.
4. Keep the graceful-degradation branch (silent reverse stream + fixed mic
   volume) as the ultimate fallback in case the new library path fails on a
   specific OS build.

## Related files

- `src/dotnet/App.Maui/Platforms/Windows/Audio/WindowsAudioCapture.cs`
- `src/dotnet/App.Maui/Platforms/Windows/Audio/CustomWasapiLoopbackCapture.cs`
- `src/dotnet/App.Maui/App.Maui.csproj` — `TrimmerRootAssembly` entries + NAudio `PackageReference`
- `docs/native-aot.md` — Known Issues section (same pattern as WindowConfigurator / WindowsAppIconBadge)
