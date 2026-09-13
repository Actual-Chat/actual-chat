Windows records natively (`MauiRecorderEngine` → `WindowsAudioCapture`) and its echo
cancellation is the **WebRTC APM** with a **WASAPI loopback capture of the default render
endpoint** as the far-end reference — not the browser's `getUserMedia`. Apple and Android
use the OS voice-processing unit and never touch the APM. See
[[windows-app-audio-diagnosis]] for why the app can't be logged.

**RESOLVED.** The symptom (TV audio transcribed as speech) was two C# bugs, both fixed:
`SetDelay` passed 640 *samples* into a *milliseconds* API — clamped to 500 ms, and set
once instead of per frame, though WebRTC clears that state after every `ProcessStream` —
and the AGC re-wrote the hardware mic volume on every 10 ms frame because `currentVolume`
only updated via an async COM notification. Alex confirmed the fix by ear: TV completely
absent, own speech intact.

**The shipped AEC is good**: 25.3 dB echo removal on echo-only frames, and transparent to
near-end speech (0.0 dB loss when no echo is present). Any smaller number in an older note
was a measurement artifact — see the traps below.

## Measurement traps that cost most of a day

- **Aggregate ERLE is diluted by double-talk.** Averaging over all reference-active frames
  reads ~10 dB; the AEC is *supposed* to preserve near-end, so double-talk frames drag it
  down. Always split by mic loudness quartile — Q1 (quietest, echo-only) is the real number.
- **Short-burst acoustic benches read far too low** (5–6 dB) because 1 s bursts give AEC3
  almost no continuous far-end to converge on. A long real session reads 25 dB.
- **Never trust a single acoustic shot.** Interleave stock/variant/stock/variant and repeat.
  Mic level swung ~3x between consecutive runs (webcam auto-gain), and a single-shot
  comparison once showed a spurious +8 dB "improvement" that vanished under repetition.
- **Synthetic far-end-only echo saturates the metric** at ~66 dB regardless of tuning: with
  no near-end the residual suppressor gates the output entirely. Any synthetic bench needs
  a near-end floor, and even then it can't discriminate.
- **Always run an AEC-off control in the same process**; it must read ~0 dB.
- With a 390 ms echo path, "no echo right now" means the reference was silent *390 ms ago*,
  not now. Classify frames on the delayed window or you get zero near-end-only frames.
- Alex's rig is representative, not a bad bench — 15% on that TV is normal listening level
  and the Insta360 mic sits directly in front of it. Don't ask to raise the volume.

## The right instrument: WebRtcApmTap

`src/dotnet/App.Maui/Platforms/Windows/Audio/WebRtcApmTap.cs` records the exact
`micIn`/`loopIn`/`output` frames handed to the APM plus a per-frame `CpuTimestamp` log,
buffered in RAM and written as WAVs on stop. Enable by creating an `aec-tap.on` file in the
app data dir; output lands in `AecTaps/<timestamp>/`. **Deterministic replay of a real
capture is the only trustworthy comparison** — no room, no auto-gain, only the variable
under test. Unpackaged dev builds put app data under
`%LOCALAPPDATA%\Actual Chat Inc\ActualChatInc.ActualChat.Local\Data`, not a package
LocalState folder.

## Established facts

- Render→mic delay is a steady **390 ms** (LG TV over HDMI). AEC3 aligns it fine — search
  span is 512 ms, `MaxDelay()` 612 ms — so the long delay is *not* a problem.
- The reverse stream stays aligned in the real app: measured lookback 350–355 ms, stable
  across 57 s, correlation 0.74–0.94, and **no mic sample drops** (shortfall is a constant
  ~210 ms of startup latency, not accumulating).
- Loopback delivers 16 kHz mono float via `AutoConvertPcm` even though `IsFormatSupported`
  returns false; ~160 samples per 10 ms `DataAvailable`.
- If the `QuantumStarted` handler stalls, Windows **drops** mic samples (68% loss measured
  at 25 ms/quantum) and neither `IsDiscontinuous` nor `RelativeTime` reports it. A dropped
  chunk shifts mic against reference permanently; nothing detects or corrects it. Latent
  robustness gap — backfilling with zeros (detect via `CpuTimestamp` vs sample count) is
  the fix if it ever bites.
- AEC3 tolerates a stream gap fine if *both* streams skip together, and re-locks in <1 s
  after a relative shift — but a shift that drives lookback to ≤0 is permanently fatal.

## Dead ends — do not re-run

- **Rebuilding `webrtc-apm.dll` from source is a REGRESSION**: 16.1 dB vs the shipped
  prebuilt's 25.3 dB on the same capture. Not build flags (AVX2 on, `inline-sse` default
  true, both verified) — a **source revision** difference. The shipped DLL exports
  `webrtc_apm_config_dump`/`webrtc_apm_string_free`, absent from
  `LSXPrime/webrtc-audio-processing@79d02f8`; SoundFlow built from a different snapshot.
- **AEC3 tuning doesn't help.** A sweep over filter length, delay headroom, reverb decay,
  near-end masking thresholds and dominant-nearend detection found defaults optimal —
  every variant neutral or worse, filter-length increases cost ~4 dB.
- **AEC dump is compiled out** (`rtc_enable_protobuf=false` → null factory). Returns 0 and
  writes nothing, even for an invalid path.
- Disproven: AGC masking the AEC; multiple `webrtc_apm_create()` instances interfering; the
  `SetDelay` units bug or the mic hold-back being the *cause* of low ERLE.

## Provenance

`Core.Audio/APM/` is copied from `LSXPrime/SoundFlow` →
`Extensions/SoundFlow.Extensions.WebRtc.Apm`; its native comes from
`LSXPrime/webrtc-audio-processing`, with the C shim appended to
`webrtc/api/audio/audio_processing.{h,cc}`. Natives are stored in **Git LFS**, and **only
win-x64 is ever consumed** — `App.Maui.csproj:762` hardcodes one `Content` include; the
per-RID glob in `Core.Audio.csproj` is commented out.

`Actual-Chat/webrtc-audio-processing@ba5988a` (our fork) adds
`webrtc_apm_create_with_aec3_config` + an opaque `EchoCanceller3Config` handle — the only
way to reach AEC3's internals from C, since `webrtc_apm_create()` builds the APM with no
`EchoControlFactory`. Builds with meson+ninja under MSVC x64 in ~4 min; Linux/macOS build
the same way in Docker / on the Mac Mini. Good as an instrument, **not shippable** while
the from-source build regresses.
