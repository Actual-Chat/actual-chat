# TS-Side Audio Pull — Plan & Status

**Branch:** `feat/ts-audio-pull`
**Started:** 2026-04-21
**Status:** Scaffolding complete, feature-flagged on; production polish + rollout remaining.

## Goal

Move audio-playback frame flow from the .NET `AudioTrackPlayer` +
`WebAudioPlaybackEngine.PushFrame` path to a TypeScript-native pipeline that
subscribes to `ILiveAudioStreams` directly, demultiplexes the stream, and
pipes Opus packets straight into `OpusDecoderWorker` via `postMessage`. The
existing path pushed every 20-ms Opus frame through Blazor JSON JS-interop
(`byte[]` → base64 → JSON → JS `Uint8Array`), which dominated main-thread
CPU during listening:

```
[Old profile, listening on 1 chat, 30 s window]
Blazor interop + postMessage:  ~1.37 s main-thread CPU
  frame@audio-player.ts        126 ms
  _processIncomingData         354 ms
  beginInvokeJSFromDotNet      216 ms
  processJSCall                134 ms
  send (Blazor circuit)        181 ms
```

After cutover the same scope is gone — replaced by a `~500 ms` TS-pull
pipeline (`handleStart` + `onOpusPacket` + `feed`), for a net ~300–400 ms
inclusive-time saving per 30 s. Room to grow with more concurrent listeners.

## Architecture

```
Server (.NET)                          Browser (TS)
──────────────                          ─────────────
ILiveAudioStreams.GetStream(sessionId, chatId)
  └─► RpcStream<LiveStreamItem>        ◄── main-thread Api.peer (msgpack6/WS)
        │
        │  [LiveStreamStart N]                 ↓
        │  [LiveAudioFrame N Data Offset]      runLiveStreamDemuxer
        │  [LiveStreamEnd N]                   │
        │  [LiveStreamReset]                   │  (demuxed per-streamIndex)
                                               ↓
                                               onStreamStarted(info, header)
                                               │
                                               ↓
                                               PullAudioRenderer.create
                                                 │
                                                 │  shares AudioPlayer's
                                                 │  decoderWorkerInstance
                                                 ↓
                                               feed(packet)
                                                 │
                                                 │  rpcSendNoWait
                                                 ↓
                                               OpusDecoderWorker
                                                 │
                                                 │  (MessageChannel)
                                                 ↓
                                               FeederAudioWorklet
                                                 │
                                                 ↓
                                               AudioContext destination
```

No Blazor interop on the per-frame path. One `postMessage` + one transferable
per Opus packet. Main-thread renderer sits in lifecycle/UI roles only.

## What Landed (Phase 1 — scaffolding)

All behind `AudioSettings.UseTsAudioPull` flag (default: project-dependent).

### TS, shared
- **`src/nodejs/src/api/live-audio-streams-api.ts`** — `defineRpcService('ILiveAudioStreams', …)` + DTOs (`LiveStreamItem` union, `LiveStreamInfo`, `LiveStreamSettings`, `AudioFormat`), `parseLiveStreamItem`.
- **`src/nodejs/src/audio/actual-opus-stream-parser.ts`** — `parseActualOpusStreamHeader` (one-shot) + `isActualOpusStreamHeader` prefix-sniff + container streaming parser (kept for non-pull callers).
- **`src/nodejs/src/audio/live-stream-demuxer.ts`** — `runLiveStreamDemuxer(source, onStreamStarted, signal)` — mirror of .NET `LiveStreamDemuxer`. Parses union items, maintains per-streamIndex queues + consumers.
- **`src/nodejs/src/audio/live-audio-pull.ts`** — `startLiveAudioPull(options, consumer)` driver. Opens `ILiveAudioStreams.GetStream`/`GetReplayStream`, runs demuxer, handles A_OPUS_S-header-or-raw-Opus detection, honours `ownAuthorId` skip, holds `Api.requireConnection(scope)` for the peer's lifetime.
- **`src/nodejs/src/rpc.ts`** — `rpcSendNoWait(port, method, args, transferables?)` helper + fast-path for `rpcNoWait` in `getProxyMethod`.
- **`src/nodejs/src/actuallab-rpc/msgpack-map-patch.ts`** — extended existing patch to handle `bigint` as int64 (0xd3) / uint64 (0xcf). Required for `Moment.EpochOffsetTicks` replay args that exceed `Number.MAX_SAFE_INTEGER`.

### TS, UI.Blazor.App
- **`Components/AudioPlayer/pull-audio-renderer.ts`** — `PullAudioRenderer` — minimal Blazor-free renderer. Owns a feeder-worklet node + OpusDecoder session per sub-stream; shares the process-wide decoder worker via `getDecoderWorkerInstance`. `feed(packet)` uses `rpcSendNoWait` directly (no Proxy).
- **`Components/AudioPlayer/live-audio-pull-consumer.ts`** — `createDefaultLiveAudioPullConsumer`, `startLiveAudioListen`, `startLiveAudioReplay`, `initLiveAudioPullRpc`, **and `LiveAudioPullBridge`** — numeric-token registry exposed to .NET as `window.blazorApp.LiveAudioPullBridge`.
- **`Components/AudioPlayer/audio-player.ts`** — exposes `getDecoderWorkerInstance` / `getDecoderWorker`. `frame(bytes)` hot path uses `rpcSendNoWait` direct to worker (even on the legacy path).
- **`exports.ts`** — adds `LiveAudioPullBridge` to `window.blazorApp.*`.

### .NET
- **`Api/Audio/AudioSettings.cs`** — `UseTsAudioPull` flag (bool, default depends on deployment).
- **`UI.Blazor.App/Components/AudioPlayer/TsAudioPullBridge.cs`** — .NET wrapper that InvokeAsync's the JS bridge. `StartListen(session, chatId, ownAuthorId, ct)` and `StartReplay(session, chatId, startAt, rewindOffset, speed, ownAuthorId, ct)` return `TsAudioPullHandle : IAsyncDisposable` that auto-stops on CT or explicit disposal. Sends `long` tick args as `InvariantCulture` strings; JS side parses to `BigInt`.
- **`UI.Blazor.App/Module/BlazorUIAppModule.cs`** — DI-registered `TsAudioPullBridge` (scoped).
- **`UI.Blazor.App/Services/Playback/ChatListener.cs`** — when flag is on: resolves `ownAuthorId` (via `Authors.GetOwn`, honouring `ListenOwnAudio` debug flag), calls `TsAudioPullBridge.StartListen`, parks on CT. Legacy path unchanged when flag is off.
- **`UI.Blazor.App/Services/Playback/ChatReplayer.cs`** — same pattern for replay.

### Tests
- **`tests/ts/unit/actual-opus-stream-parser.test.ts`** — 9 cases: header parse, chunked header/packet, prefix sniff, fresh-buffer-per-packet (transfer safety).
- **`tests/ts/unit/msgpack-bigint.test.ts`** — 6 cases covering moderate/large/negative BigInt, int64/uint64 overflow, out-of-range error, nested BigInt in object.
- **`src/nodejs/app.audio-pull-test/`** — standalone push+pull smoke test (`npm run test:audio-pull`). Signs in, pushes synthetic `AudioFrameDto` to `IStreamServer.PushAudio`, subscribes via `ILiveAudioStreams.GetStream`, demuxes, reports round-trip frame/packet counts. Pass criteria: startedStreams ≥ 1, packets round-tripped.

### Measured impact

30-s traces, `chat=the-actual-one`, 1 active speaker:

| Metric | Before | After | Δ |
|---|---:|---:|---:|
| CrRendererMain busy (self) | 1434 ms | 1331 ms | **−103 ms (−7%)** |
| Blazor interop chain (inclusive) | ~1370 ms | ~240 ms | **−1130 ms** |
| TS-pull pipeline cost (inclusive) | 0 | ~500 ms | +500 ms |
| Net main-thread inclusive | — | — | **~−600 ms** |

Savings scale linearly with listener count and stream activity.

## Open Problems (Phase 2 — what to ship next on the branch)

### P0 — Correctness / UX parity with legacy path

1. **Join-late `skipTo`** — when a user opens Listen mid-recording, the server replays from the stream start. TS-pull plays the whole backlog at 1× before catching up. Legacy path computed `skipTo = (playAt - streamInfo.BeginsAt).Positive()` in `ChatListener.OnStreamStarted` and dropped frames whose Offset was before `skipTo`.
   - **Location:** `live-audio-pull.ts::handleStart` — add per-packet Offset tracking (demuxer currently discards `LiveAudioFrame.Offset`), compare against `skipToMs` passed via `LiveStreamStartedInfo`.
   - **Wiring:** demuxer needs to forward per-frame Offset. Simplest: change `frames` from `AsyncIterable<Uint8Array>` to `AsyncIterable<{ data: Uint8Array; offsetTicks: number }>`.
   - **Scope:** ~50 LOC in demuxer + consumer + caller.

2. **Replay `skipTo` / `speed` parity** — server already handles speed server-side (frame-skip in `ReplayStreamMuxer`), but `startAt` wallclock positioning assumes the recording aligns with request. Needs verification across rewind-offset edge cases.

3. **`ResilientStream` for live** — legacy path wraps `ILiveAudioStreams.GetStream` in `ResilientStream<LiveStreamItem>` (`src/nodejs/src/resilient-stream.ts` exists). TS-pull currently doesn't — a WS reconnect loses the stream permanently.
   - **Location:** `live-audio-pull.ts::startLiveAudioPull` — replace direct `client.GetStream()` with `ResilientStream` when `mode === Live`. Replay shouldn't reconnect (reruns from start would be wrong).

### P1 — Orchestration policies not yet ported

These currently run **only** on the legacy path. When `UseTsAudioPull=true` they're silent no-ops. For some users/scenarios, parity is required before flipping the flag on by default.

4. **Notification sound on new message after idle** — `ChatListener.OnStreamStarted` plays `Tune.NotifyOnNewAudioMessageAfterDelay` when `streamInfo.BeginsAt - state.LastStreamBeginsAt > IdleListeningNewMessageTrigger` (5 min default). Needs a TS equivalent triggered from demuxer `onStreamStarted`.

5. **Sleep-drift compensation** — `SleepDuration.Value` is read on .NET; if device slept, `minPlayAt` snaps forward. TS-pull joins at whatever BeginsAt the server gives.

6. **`CanContinuePlayback` / listen-state guard** — legacy path drops streams when `ChatAudioUI.SetListeningState(false)` fires. TS-pull depends only on the .NET `ChatListener.Play` cancellation token; that should be equivalent in practice, but needs a manual QA pass around app-background / screen-lock / chat-switch scenarios.

7. **Latency report** — `StreamClient.ReportAudioLatency(latency, ct)` fires per stream start on legacy. Missing on TS-pull.

8. **ChatEntry metadata lookup for replay** — `ChatReplayer.OnStreamStarted` uses `NewEntryReader.Get(entryId)` to enrich `ChatAudioTrackInfo` with the actual `ChatEntry`. Not used for playback itself but influences UI state (chat-entry highlight, share, etc.).

### P2 — Quality / polish

9. **Buffer-low back-pressure** — legacy `AudioTrackPlayer._whenBufferLowSource` gates frame pushing. TS-pull has no equivalent; decoder queue can grow unbounded on slow playback. Not observed in practice yet (network-paced delivery), but worth a cap + drop-oldest policy for safety.

10. **AudioContext unlock timing** — `PullAudioRenderer.create` calls `audioContextSource.createRef` which awaits `whenReady()`. If the first listen happens before the user has unlocked the AudioContext (no prior gesture-initiated playback), it hangs silently. Legacy path has the same issue but is mitigated by the existing AudioPlayer UI flow. Audit entry points when flag-on-by-default is attempted.

11. **Smoke-test harness using production Api peer** — current `app.audio-pull-test` builds its own peer with explicit `peer.start()` + `peer.whenConnected()`, bypassing `Api.requireConnection`. That gap caused a major silent-hang bug we hit during development (peer never opens the WS if no scope is held). Rewrite the harness to go through `initLiveAudioPullRpc()` + `Api.requireConnection('test')` so future regressions in the gate logic are caught.

12. **Log-level cleanup** — `LiveAudioPull` and `LiveStreamDemuxer` are defaulted to `LogLevel.Debug` for diagnosis. Step back to `Warn` once the feature is stable.

13. **ApiArray<LiveStreamInfo> decode** for `ILiveAudioStreams.List` — DTO typed as `LiveStreamInfoDto[]` but the wire is `ApiArray<>` which needs a small resolver. Not blocking the pull path; needed only if we ever surface `List` in TS.

### P3 — Follow-ons unblocked by TS-pull

14. **Delete `WebAudioPlaybackEngine.PushFrame` path** — once TS-pull is the default and flag-off regressions are verified, delete the .NET-side frame pump entirely (legacy playback engine, `AudioTrackPlayer.ProcessMediaFrame`, etc.). Sizable .NET code removal.

15. **SAB ring for Opus frames (originally Option C)** — deferred from earlier optimisation round. With TS-pull, the hot path is already TS-local; SAB would cut the main-thread → decoder-worker `postMessage` cost (~150 ms per 30-s trace). Needs own design doc.

16. **Video path parity** — video already uses a similar TS-side pull (`video-player.ts::startPull`). Audit whether the patterns converge — e.g. shared `Api.requireConnection` lifecycle, shared `parseLiveStreamItem`-style union helper, shared `TsXxxPullBridge` pattern.

## Rollout Sequencing

On `feat/ts-audio-pull`:

- [x] Phase 1: scaffolding + feature flag (this branch's initial work).
- [ ] Phase 2 P0 items (#1–#3). Target: ship flag-on for internal testing.
- [ ] Phase 2 P1 items (#4–#8). Target: flag-on-by-default in dev deployment.
- [ ] Phase 2 P2/P3 (#9–#16) as follow-up PRs, possibly on their own branches.

## Testing Checklist

Before flipping `UseTsAudioPull = true` as default in any environment:

- [ ] Live listen, single speaker, end-to-end: audio quality same as legacy.
- [ ] Live listen with user also speaking: no self-echo (confirms `ownAuthorId` filter).
- [ ] Join mid-recording: audio starts near real-time (requires P0 #1 skipTo).
- [ ] Replay, single entry: audio plays from requested start.
- [ ] Replay, multi-entry chat: gaps handled correctly, speed changes honoured.
- [ ] WS reconnect during live listen: audio resumes (requires P0 #3 ResilientStream).
- [ ] Chat switch mid-listen: old driver stops, new one starts cleanly (`Api.releaseConnection` fires).
- [ ] App backgrounded then foregrounded: audio resumes or gracefully ends.
- [ ] Heavy chat (3+ concurrent speakers): no starvation, no decoder-queue blow-up.
- [ ] Smoke test `npm run test:audio-pull` green against local dev server.

## References

- Baseline trace: `tmp/profiles/Trace-20260421T124844.json.gz` (Blazor-pump path).
- Post-cutover trace: `tmp/profiles/Trace-20260421T143818.json.gz` (TS-pull).
- Related server code: `src/dotnet/Streaming.Service/Services/{LiveAudioStreams.cs, LiveStreamMuxer.cs, ReplayStreamMuxer.cs}`.
- Related client code (legacy): `src/dotnet/UI.Blazor.App/Components/AudioPlayer/{AudioTrackPlayer.cs, WebAudioPlaybackEngine.cs, audio-player.ts}`.
