# 08 — Diagnostics and tunables

What's measured, where it surfaces, and which constants you may want to
know about.

## Server-side meters

| Instrument | Kind | Source |
|---|---|---|
| `AppMeters.AudioStreamCount` | `UpDownCounter<int>` | `StreamStore<AudioFrame>` publish/expire |
| `AppMeters.AudioLatency` | `Histogram<double>` ms | `ILiveAudioStreams.ReportAudioLatency` (receiver-reported end-to-end) |
| `AppMeters.TranscriptStreamCount` | `UpDownCounter<int>` | `_transcriptStreams` publish/expire |
| `StreamingInstruments.AudioFrameDeserializeDuration` | Histogram, µs | `CachingAudioFrameFormatter` deserialize |
| `StreamingInstruments.AudioFrameSerializeDuration` | Histogram, µs | same, serialize |
| `StreamingInstruments.AudioFrameSizeBytes` | Histogram, int | encoded chunk size |
| `StreamingInstruments.AudioActiveConsumers` | `UpDownCounter<int>` | `LiveAudioStreams.GetStream` enter/exit |
| `StreamingInstruments.AudioFramesReceived` / `AudioBytesReceived` | `Counter<long>` | publish path |
| `StreamingInstruments.AudioFramesSent` / `AudioBytesSent` | `Counter<long>` | per-consumer fan-out |

OTEL pipeline / Grafana wiring lives in
`Core.Server/Diagnostics/AppMeters.cs` and the repo-root
`otel-collector-config.yaml`.

## Server log markers

- `"ProcessAudio: source clock skew {ClockDeltaMs}ms"` — clock-skew
  override fired (`MaxBeginsAtDrift = 5 s`).
- `"ProcessAudio: frame silence timeout"` — no frames in 2 s, stream
  cancelled.
- `"Register: evicting stale stream {OldStreamId} for author"` —
  per-author merge in `LiveAudioBackend.Register`.
- `"GetOrFetchRemoteAudio: caching ..."` — cross-shard cache miss,
  initial fetch.
- `"ListenAtFromRedisFailed - falling back to chat entries"` —
  `LiveAudioBackend` fallback recovery from `ChatsBackend`.
- Per-segment: transcriber type / model / language candidates; first
  non-empty interim transcript with timestamps.

## Client-side stats and reporting

### Recorder side

`AudioRecorder.AudioRecorderState` (C# side, `MutableState<…>`):

```
ChatId, IsRecording, IsSignalDetected, IsConnected, IsVoiceActive
```

`AudioRecorder.AudioDiagnosticsState` (debug):

```
IsPlayerInitialized, IsRecorderInitialized
HasMicrophonePermission
IsAudioContextSourceMaintained, IsAudioContextRunning
HasMicrophoneStream
IsVadActive, LastVadEvent, LastVadFrameProcessedAt
IsConnected, IsSignalDetected, LastFrameProcessedAt
VadWorkletState, EncoderWorkletState (+ timestamps)
```

JS-side state lives in `OpusMediaRecorder.state` and the
`RecordingActivity` Lit component (drives the animated mic SVG).

### Player side

`PlaybackLagTracker` — per-author EMAs of audio and video presentation
lag, fed by `OnPresentationLag` callbacks. Used by
`LiveAudioCatchUpPolicy` even when sync is disabled (the EMAs are
still maintained).

Feeder worklet emits state changes for `playbackState` (`playing` /
`starving` / `ended` / `paused`) and `bufferState` (`ok` / `low`).
`AudioPlayer` forwards them through Blazor as `OnPlaying(...)` and
`OnPresentationLag(ms)`.

## Audio Diagnostics modal (on-device)

A read-only troubleshooting panel for the classic iOS failure where inbound
playback dies mid-conversation (AVAudioSession stuck suspended/interrupted)
while UI "tunes" still play. It surfaces the native session state, the Web
Audio `AudioContext` state, and per-track feeder state side by side — the
tell-tale is `_isSuspended` / `AudioContext.state == 'suspended'` while streams
are live and feeders are `starving` — and offers reactivate/resume actions.

- Compute service — `src/dotnet/UI.Blazor.App/Services/AudioDiagnostics/AudioDiagnosticsUI.cs`
- Modal + JS collector — `UI.Blazor.App/Components/AudioPanel/{AudioDiagnosticsModal.razor,audio-diagnostics.ts}`
- Native session snapshot — `App.Maui/MaciOS/Audio/{AppleAudioFocusUI,AudioSession}.cs`
- Enable via `UserAppSettings.IsAudioDiagnosticsEnabled` (DeveloperTools toggle) —
  an account setting, so it syncs across the user's devices and is picked up
  reactively; mirrors the Video Diagnostics modal.

## Debug hooks

- **`DebugUI.EnableAudioSync(true|false)`** — toggle the
  `ChatAudioUI.IsAudioSyncEnabled` flag (only on dev instances). Default
  is **off**.
- **`Constants.DebugMode.ListenOwnAudio`** — debug build flag; lets
  the receiver play your own outgoing audio (off in production).
- **`OpusMediaRecorder.suspendHeartbeat(durationMs)`** — simulate a hung
  main thread to verify worker watchdog.
- **`opus-encoder-worker.setRecorderOffset(offsetMs)`** — bias source
  timestamp for clock-drift simulation (drives the server-side
  `MaxBeginsAtDrift` path).
- **`opus-encoder-worker.disconnectApi()`** — kill the RPC peer to test
  the streamer's reconnect/replay logic.
- **`Settings.UseFakeTranscriber`** — replace Google/Deepgram with
  `FakeTranscriber` (canned word patterns). Useful for testing
  transcript wiring without spending API credits.

## Tunable constants

All in `src/dotnet/Api/Constants.Audio.cs` unless noted.

### Frame layout

| Name | Default | Notes |
|---|---|---|
| `FrameRate` | 50 fps | Opus frame cadence |
| `FrameDurationMs` | 20 ms | Opus frame size |
| `OpusFrameDuration` | 20 ms | TimeSpan alias |
| `Channels` | 1 | mono |
| `RecordingSampleRate` | 16 000 Hz | mic input |
| `PlaybackSampleRate` | 48 000 Hz | decoder output |
| `OpusFrameLength` | 320 samples | 16 kHz × 20 ms |
| `PcmFrameLength` | 960 samples | 48 kHz × 20 ms |
| `Bitrate` | 32 000 bps | Opus encoder target |
| `PreSkip` (default) | 312 samples | codec warm-up at 48 kHz |

### VAD

| Name | Default |
|---|---|
| `VadFrameDurationMs` | 32 ms (Silero) |
| WebRTC VAD window | 30 ms (in worker code) |
| `VoicePreRollFrameLimit` | 15 frames (~300 ms) |
| Silero load delay | 2 s after page interactive |

### RPC

| Name | Default | Notes |
|---|---|---|
| `RecordingRpcStreamAckPeriod` | 5 frames (≈100 ms) | client → server |
| `DeliveryRpcStreamAckPeriod` | 5 frames (≈100 ms) | server → client |
| `AllowReconnect` (publish) | true | client resumes on peer change |
| `AllowReconnect` (subscribe) | true (false on `LegacyGetStream`) | per `MediaRpcStreamOptions` |

### Server timings

| Name | Default | Notes |
|---|---|---|
| `MaxStreamDuration` | 3 min | hard cap on a single recording session |
| `MaxBeginsAtDrift` | 5 s | clock-skew override threshold |
| `FrameSilenceTimeout` | 2 s | publisher stall watchdog |
| `MaxEntryDuration` | (per `Constants.Chat`) | chat-entry duration cap |
| `StreamExpirationDelay` (`AudioSettings`) | 10 s | StreamStore idle expiry |
| `StreamTtl` (`LiveAudioBackend`) | 3 min | Redis state TTL = 5 min |
| `StaleAudioTrimWindow` (`LiveStreamMuxer`) | 3 s | live trim |
| `EvictionDelay` (`LiveStreamMuxer`) | 4 s | post-end stream removal |
| `ReconnectDelay` (`LiveStreamMuxer`) | 1 s | List-watch retry |

### Transcription

| Name | Default | Notes |
|---|---|---|
| `Transcription.ThrottlePeriod` | 0.2 s | interim diff coalescing |
| `Transcription.CancellationDelay` | 3 s | grace before cancelling transcribers |
| `Transcription.SilentPrefixDuration` (Google) | 3 s | leading silence trim |
| `Transcription.SilentSuffixDuration` (Google) | 0 s | trailing silence trim |

### Playback

| Name | Default | Notes |
|---|---|---|
| `StartBufferSize` | 5 frames (100 ms) | decoder warm-up |
| `MinBufferSize` | 2 frames | hysteresis floor |
| `BufferHysteresisSize` | 3 frames | hysteresis width |
| `PlaybackTargetBufferSizeWithVideo` | (a few × 100 ms) | extra buffer for A/V-paired tracks |
| `PlaybackHardSkipThreshold` | 2 s | A/V sync hard skip (gated by `IsAudioSyncEnabled`) |
| `PlaybackMaxSpeedUpDuration` | 5 s | A/V sync speed-up bound |
| `PlaybackSpeedUpDropEveryNFrames` | 4 | speed-up drop pattern |
| `PlaybackCatchUpCommandCooldown` | 1 s | between sync corrections |
| `PlaybackLagStaleAfter` | ~1.5 s | lag sample freshness |
| `AudioCatchUpDeadband` | 200 ms | sync no-op band |
| `AudioCatchUpBaselineDelta` | -100 ms | "audio slightly ahead" target |
| `PacingHeadStartDuration` (`AudioTrackPlayer.cs`) | 30 ms | initial frame burst |
| `PacingDuration` (`AudioTrackPlayer.cs`) | 200 ms | real-time pacing window |
| `ReportPlayingMaxIntervalMs` (`audio-player.ts`) | 1000 ms | OnPlaying heartbeat |
| `LagReportIntervalMs` (`audio-player.ts`) | 500 ms | OnPresentationLag rate |

## Where to look when something is wrong

| Symptom | First place |
|---|---|
| Recorder says "no signal" | `AudioRecorder.AudioDiagnosticsState`, getUserMedia constraints, AGC disabled on Android? |
| VAD never fires | `audio-vad-worker.ts` — Silero loaded? Resampler running? Mic at 16 kHz after resample? |
| First syllable clipped | `VoicePreRollFrameLimit` increased? `preSkip` correct? |
| Transcript is wrong language | `AudioSegmentLanguage.Resolve` candidates; `DetectLanguage` triggering Deepgram? |
| Live audio stutters | feeder worklet `bufferState='low'` — pacing too aggressive? Decoder behind? |
| Hard pop at stream start | `preSkip` mismatch between sender and decoder; pacing head-start too short |
| Cross-shard duplicate fetches | `RemoteAudioStreamCache.Store.Get` not finding hit — TTL expired? |
| Replay too fast/slow | `ReplayStreamMuxer` pacing; `speed` parameter; per-frame skip pattern |
| iOS lock-screen has no metadata | `DestinationFallbackTrait` not enabled? `MediaSession.metadata` set? |
| Audio focus issue on Android | `AudioFocusUI` interactions; `OnAudioFocusLost(canDuck)` handler |
| A/V sync drift | A/V sync is **off by default**. `DebugUI.EnableAudioSync(true)` to enable; verify `PlaybackLagTracker` sees both audio and video samples for the same `AuthorId` |
