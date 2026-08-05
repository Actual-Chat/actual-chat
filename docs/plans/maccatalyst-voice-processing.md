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

## The fix (2026-07, #4060)

The missing piece was identified by studying LiveKit's `AudioEngineDevice`
(`webrtc-sdk/webrtc`, `modules/audio_device/audio_engine_device.mm`) — a
known-working AVAudioEngine + VP implementation on Mac Catalyst. Three
requirements our graph violated:

1. **The input node must be part of a pulled render chain.** On iOS a bare
   tap on the VP input works; on Mac nothing pulls the input node unless it
   is connected downstream, so the tap fires once and goes silent. LiveKit
   wires `inputNode → mixer → AVAudioSinkNode`; we wire
   `inputNode → muted mixer → MainMixerNode` (which also gives the VP
   downlink its output-side clock — the piece the silent-player experiment
   got right).
2. **The input connection must use ≤ 2 channels.** Mac input nodes can
   expose multi-channel formats VPIO can't handle; LiveKit always connects
   at mono. We connect at mono @ hardware sample rate.
3. **VP must be enabled while the engine is stopped, and all format queries
   must happen after enabling it** — enabling VP changes the input node's
   format (VPIO brings up an aggregate device). Our resampler was built
   from the pre-VP format.

Implemented in `AudioEngine.EnableVoiceProcessing()` (one-shot, engine-level)
plus `ConnectVoiceProcessingPullChainUnsafe()` for the Mac-only pull chain;
`AppleAudioCapture` now calls it on both platforms before querying formats.

**Round 2 (same day):** the first cut failed on-device — engine start hit
`kAUInitialize` error **-10875** on the VPIO *output* element, because (a) the
implicit `MainMixer → OutputNode` connection captured the output format while
it was still `0ch/0Hz` (VPIO aggregate device not built yet; visible as
`AudioConverter ... to 0 ch, 0 Hz, status -50` spam in the unified log), and
(b) the engine start raced the VPIO uplink/downlink DSP bring-up. Fixes,
all straight from LiveKit's implementation: validate input/output formats are
non-zero before wiring (throw = the recorder's retry loop re-enters cleanly),
connect `MainMixer → OutputNode` explicitly with a valid mono format, and on
Mac start the VP engine via `Prepare()` + 100 ms + up to 3 start attempts
(`StartWithVoiceProcessingUnsafe`).

**Why the split Playback/Recording engines still get AEC:** on macOS the
VPIO reference signal is captured at the output-device level (that's how
FaceTime/Safari cancel other apps' audio), so voice played by the separate
Playback engine is still cancelled from the Recording engine's VP'd input.

**Round 3 (same day):** with round 2 the recording engine started and VP ran
(aggregate device built: mic + speaker ref, uplink/downlink DSP live), but two
issues remained on-device:

1. `AUVPAggregate: incorrect pull size (err=-50)` on every render cycle
   while recording — the session's 20 ms (960-frame) preferred IO buffer
   fights the VP aggregate's 512/1024-frame quantum. Fix: skip
   `SetPreferredIOBufferDuration` on Mac Catalyst (`AudioSession.ConfigureUnsafe`).
2. After `StopRecording` → `AVAudioEngine.Stop`, the VPIO aggregate's IO
   thread isn't torn down; the next engine start blocks ~15 s in AQME and
   fails `kAUStartIO` with **-66681** before recovering. Fix: on Mac, stop
   the IO units explicitly (`AudioOutputUnitStop` via `IONode.AudioUnit.Stop()`)
   before `_engine.Stop()` when VP is enabled — LiveKit does exactly this
   with the same rationale in a code comment.

**Round 4 (same day):** two on-device findings.

1. **Tap + render chain double-pull VPIO.** With both the input-node tap and
   the muted pull chain attached, VP's uplink DSP glitched every cycle
   (`incorrect pull size (err=-50)` storm) — full frame rate but chopped
   content. Fix: no tap on Mac; capture through an `AVAudioSinkNode` as the
   single puller (`inputNode → capture mixer → sink`,
   `AudioEngine.AttachVoiceProcessedInputSink`), the exact LiveKit topology.
   Result: steady 48k frames/s, zero pull-size errors, clean audio.
2. **macOS AEC references only the VPIO unit's own output**, not the whole
   device (unlike iOS). Audio played by the separate Playback engine (or
   other apps) is NOT cancelled. Fix: `VoicePlayer` routes tracks that start
   while `AudioEngines.Recording.IsRunning` through the Recording engine, so
   in-app voice playback renders via VPIO's output and becomes the AEC
   far-end reference. Limitations (accepted): a track already playing on the
   Playback engine when recording starts isn't cancelled; a Recording-engine
   track is cut off if recording stops mid-track (`StopRecording` stops the
   shared engine); other apps' audio can't be cancelled at all on Mac.

**Round 5 (same day):** enabling VP made macOS duck all other apps' audio
(system music nearly muted while recording) — that's Apple's default
"other audio ducking" (macOS 14+ / WWDC23). Wrong for a desktop app; disabled
via `AVAudioInputNode.voiceProcessingOtherAudioDuckingConfiguration`
(`EnableAdvancedDucking = false`, `DuckingLevel = Min`) in
`InputNode.DisableOtherAudioDucking`, called right after VP enable on
Catalyst 17+. Note `Min` is the minimum, not zero — if residual ducking is
still noticeable, the next lever is adding
`AVAudioSessionCategoryOptions.MixWithOthers` to the Recording category on
Catalyst.

**Round 6 (2026-07-29):** ducking persisted after recording stopped, until app
exit. Cause: VP enable was one-shot sticky, and a Recording-bound `VoicePlayer`
(the round-4 AEC-reference routing) restarts the stopped Recording engine on
`Play`/`Resume` — with VP still armed, that revived the whole VPIO unit (mic
live + ducking) with no capture consumer, and nothing ever stopped it again.
Fix: `AudioEngine.DisableVoiceProcessing` (stop, unwire the capture mixer,
`setVoiceProcessingEnabled(false)`), called from `StopRecording` on Mac, so VP
now lives only while the mic does; each capture re-arms it via
`EnableVoiceProcessing`. Side effect: a revived Recording engine now plays
without VPIO, so Recording-bound tracks survive a mid-track recording stop
instead of dying (softens a round-4 accepted limitation).

## Fallback paths (if the fix regresses)

1. **`AVAudioSinkNode` instead of tap + muted mixer** (exact LiveKit
   topology) — needs a `SinkNode` wrapper and `AudioBufferList` → PCM buffer
   adaptation for the resampler.
2. **Direct `kAudioUnitSubType_VoiceProcessingIO` AudioUnit graph** —
   bypasses `AVAudioEngine`'s wrapper entirely; highest cost.
3. **`[engine prepare] + ~100ms delay` before start** — LiveKit's macOS
   workaround for engine-start failures when another app already holds VP;
   add to `EnsureRunning` if start errors show up.

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
