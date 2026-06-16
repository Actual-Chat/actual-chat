# Mac Catalyst: restore AVAudioEngine voice processing (AEC / NS / AGC)

## Goal
Re-enable hardware voice processing (echo cancellation, noise suppression,
automatic gain control) for the recording graph on Mac Catalyst so the desktop
app reaches parity with iOS. Today VP is disabled on Mac Catalyst — the app
records, but raw mic frames go to VAD / Opus / transcription with no AEC, NS,
or AGC applied.

## Problem (root cause)
`AppleAudioCapture.CaptureInternal` calls
`engine.Input.SetVoiceProcessingEnabled(true)` on iOS only
(`App.Maui/MaciOS/Audio/AppleAudioCapture.cs:34-35`). On Mac Catalyst the same
call brings up CoreAudio's `VoiceProcessor` (`vpio`), whose **downlink** DSP
expects a live playback side feeding it timed reference frames. Our recording
graph has no playback node attached, so the VP downlink never gets valid
sample timestamps and either:

- errors out continuously (system log floods with
  *"failed to process downlink voice proc due to 'Unknown' error"* and
  *"audio time stamp does not have valid sample time"* — see commit
  `2988a1405`); or
- delivers one initial buffer and then stops calling our input tap, so VAD
  never fires and capture appears dead even though the audio session is in
  `PlayAndRecord` with `InputEnabled=1`.

iOS routes a valid downlink reference for AEC automatically. Mac Catalyst does
not, even though the same `AVAudioInputNode.SetVoiceProcessingEnabled` API
exists and returns success.

## What we tried (does not work)
**Silent `AVAudioPlayerNode` → `MainMixerNode`.** Hypothesis: VP needs *any*
running output graph so the downlink path has a clock. Wiring a silent player
node into the main mixer **suppressed the error spam** in the system log —
which is consistent with VP now seeing some output side — but did **not**
restore steady-state frame delivery: the input tap still went silent after the
first buffer. So the missing clock is a necessary but not sufficient piece;
something else in the Mac Catalyst VP graph is still wrong.

Things not yet investigated (see "Open questions / next paths" below):
explicit AUVoiceIO unit configuration, manual format/clock matching between
input and the silent output, attaching the player to the input bus instead of
MainMixer, or bypassing `AVAudioEngine`'s VP wrapper entirely in favor of a
direct `AudioUnit` (`kAudioUnitSubType_VoiceProcessingIO`) graph.

## Current behavior (shipped)
`AppleAudioCapture.cs:34` skips `SetVoiceProcessingEnabled` when
`OperatingSystem.IsMacCatalyst()`. Trade-offs:

- ✅ Mic capture works end-to-end (frames flow → VAD → Opus → transcription).
- ❌ No echo cancellation, noise suppression, or AGC on Mac.
- Mitigation: desktops are typically used with headphones, so echo into the
  mic is a minor regression versus iOS in practice. NS/AGC absence is more
  noticeable in noisy rooms or on built-in laptop mics.

The disabled call is marked with `TODO(FC): restore AEC/NS/AGC on Mac
Catalyst.` in `AppleAudioCapture.cs:26` so it surfaces in the existing
`TODO(FC)` grep.

## Open questions / next paths

1. **Direct AudioUnit graph.** Build the recording side with a raw
   `AUGraph` / `AVAudioUnitGenericIO` around
   `kAudioUnitSubType_VoiceProcessingIO`, bypassing `AVAudioEngine`'s
   wrapper. This is what most cross-platform conferencing SDKs (WebRTC,
   LiveKit, Twilio) do on macOS, because the high-level Swift API has known
   gaps. Highest cost but highest chance of working.

2. **Match formats between silent player and input bus.** The earlier
   experiment used `MainMixerNode`'s default output format; the VP downlink
   may require sample-rate/channel parity with the input format. Worth a
   second attempt before reaching for raw AudioUnits.

3. **`AUVoiceIO` properties.** Inspect/disable individual VP sub-features
   (`kAUVoiceIOProperty_VoiceProcessingEnableAGC`,
   `kAUVoiceIOProperty_BypassVoiceProcessing`, etc.) to see whether one
   specific stage is the one timing out. If AEC is the culprit but NS/AGC
   work standalone, we can land partial parity.

4. **macOS version dependence.** The bug was observed on macOS 15.x. Check
   macOS 14 vs 16 — Apple has historically shipped CoreAudio VP fixes between
   majors. If the bug is fixed in a recent macOS, we may be able to enable
   VP behind a version guard.

5. **Use macOS-native APIs directly.** Mac Catalyst can call AppKit /
   CoreAudio HAL APIs via `[SupportedOSPlatform("maccatalyst")]`. A native
   `AVCaptureSession`-based capture path (the same one Safari uses for
   `getUserMedia`) gets AEC for free from the OS — at the cost of a second
   capture pipeline to maintain.

## Reuse (mandatory section per CLAUDE.md)

### 1. Existing abstractions to reuse
- `AudioEngine` / `InputNode` (`App.Maui/MaciOS/Audio/EngineWrapper/`) — the
  shared wrapper around `AVAudioEngine`. Any retry should go through
  `InputNode.SetVoiceProcessingEnabled` rather than touching
  `AVAudioInputNode` directly.
- `OperatingSystem.IsMacCatalyst()` — the established platform guard used
  throughout `AppleAudioCapture` and friends.
- `MaciOS/` compile convention (`App.Maui.csproj:631-632`) — files under
  `MaciOS/**` compile for both `-ios` and `-maccatalyst`. The fix lives
  there because it shares iOS code paths.

### 2. Reusability of new components
None expected — any working path is intrinsically Mac Catalyst-specific and
belongs inside `AppleAudioCapture` (or a small Mac-only helper alongside it).
If we end up with a generic "VP-capable AudioUnit graph" wrapper, it could
sit in `App.Maui/MaciOS/Audio/` next to the existing wrappers.

## Verification (when attempting a fix)
- Run the Mac Catalyst app on real hardware (not the simulator — VP behavior
  differs).
- Watch Console.app filtered on `vpio` / `VoiceProcessor` / `Audio_Pass_Through_Wire`
  for the error patterns from commit `2988a1405`. Silence there + sustained
  tap callbacks = fix is working.
- Sanity-check capture: speak a sentence, confirm VAD fires, confirm the
  transcript arrives, confirm voice message playback round-trips.
- Subjective AEC check: play loud audio through speakers while recording,
  listen back — should not echo. NS check: record in a noisy environment.
- Regression-check iOS: same scenarios on iOS device, no behavior change.

## Files touched (current state)
- `App.Maui/MaciOS/Audio/AppleAudioCapture.cs:26-35` — the `TODO(FC)` marker
  and the `IsMacCatalyst` guard around `SetVoiceProcessingEnabled`.
- `App.Maui/MaciOS/Audio/EngineWrapper/InputNode.cs:24-30` —
  `SetVoiceProcessingEnabled` wrapper (no Mac-specific logic here today).

## Related commits
- `2988a1405` — initial workaround: skip `SetVoiceProcessingEnabled` on Mac
  Catalyst (first diagnosed the VP downlink bug).
- `337aeaa6b` — earlier fix that unblocked voice recording on Mac Catalyst.
