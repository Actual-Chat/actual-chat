# Voice Dubbing — Phase 0+1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A listener with "Translated voice" on hears, in a live session, every speaker of another language dubbed into the listener's translation language by Soniox TTS (stock voice), served through the existing audio fan-out.

**Architecture:** The dub is a derived audio stream `S~lang` published into `AudioStreamingBackend`'s `StreamStore<AudioFrame>` on the node that owns source stream `S`, started lazily by `GetAudio(S~lang)` exactly the way `GetTranscript(S~lang)` starts a translation. A `DubStabilizer` feeds only the stable prefix of the already-existing translated transcript stream to an `ISpeechSynthesizer` (Soniox `tts-rt-v2` WebSocket → 48 kHz PCM → `OpusFramePump`, which encodes 20 ms Opus frames at wall-clock pace and fills gaps with silence). `ListeningStreamMuxer` asks for `S~lang` instead of `S` for speakers whose candidate languages exclude the listener's, falling back to `S` when no dub appears within `DubWaitTimeout`.

**Tech Stack:** .NET 10 / C# 14, ActualLab.Fusion RPC + compute services, `System.Net.WebSockets`, OpusSharp (already referenced by `Transcription.Service`), Blazor (UI.Blazor.App), xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-09-11-voice-dubbing-design.md` — read it first. This plan implements its phases 0 and 1 only.

**Deviations from the spec, decided while planning (keep them):**
- No separate `IDubbingBackend`: dubbing is a partial of `AudioStreamingBackend`, and its entry point is the existing `IAudioStreamingBackend.GetAudio(S~lang)`, mirroring how `GetTranscript(S~lang)` starts translation. Everything the dub needs (both transcript memoizers, the audio store) is local to the stream's owner node, so no new sharded service.
- The dub stream is **not** registered in `LiveAudioBackend` and gets no `DubOf` field: the muxer substitutes the stream id itself, and the muxed start item carries the **source's** `LiveAudioStreamInfo` with a new `DubLanguage` set. Hence no registry merge-key change; the muxer simply exempts dub entries from the per-author merge.
- Soniox emits `pcm_s16le`, not `opus`: `Transcription.Service` already ships OpusSharp, so we encode 20 ms frames ourselves. No Ogg demuxer.
- No persistence of the dub blob and no `ChatEntry.Dubs` in this phase (that is phase 4, replay).
- Not in this phase (deferred, list them in the PR): the "🎧 translated" marker on the speaking indicator, the "translating…" cue, switching to a dub mid-utterance for mixed-language speakers, clone voices (phase 2), the recorded sample UI (phase 3).

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing any C#/TS/Razor. In particular: no `Async` suffix; `.ConfigureAwait(false)` in services; bool names carry `is`/`must`/`has`; `XxxTask` for task-typed variables; Allman braces for types/methods, K&R everywhere else; control-flow statements on their own line followed by a blank line; members ordered per the guide; `sealed` by default except proxied service classes; prefer `x.IsNullOrEmpty()`; never pass `StringComparison.Ordinal` / `CultureInfo.InvariantCulture`.
- **Comments:** default to none. Only a non-obvious invariant, workaround or subtlety earns a `//` comment inside the method body (never above the declaration). Type-level `/// <summary>` only when the name isn't self-explanatory, ≤ 3 lines. No `///` on members.
- Serialization: every new serialized member gets `[DataMember]` + `[Key(N)]` (and `MemoryPackOrder(N)` only on the two settings records, which already carry it) — append keys, never renumber.
- Tests: `Should`-phrased PascalCase names, FluentAssertions, `// arrange / act / assert` comments, `because` reasons on non-obvious asserts.
- Localization: no hard-coded user-visible English in Razor; keys go into `Strings.en.json` **and all 18 other hand-written catalogs** (bg, bs, cs, de, es, fr, hi, id, it, ja, ko, pl, pt, ru, tr, uk, vi, zh), then `python3 scripts/l10n/derive-bcms.py` and `python3 scripts/l10n/derive-max.py`; typed member in `LocalizedStringsLocalizerExt.cs`.
- The style hook reports violations on every `.cs`/`.ts`/`.razor`/`.css` edit — fix everything it reports, including pre-existing lines in the same file.
- Build per project (`dotnet build src/dotnet/<Project>/<Project>.csproj`) — `*.CI.slnf` is stale locally. Never build every commit; verify the tip.
- Commit after each task. Message: conventional prefix (`feat(dubbing): …`, `test(dubbing): …`), body ends with:
  ```
  Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_018rKUpm6bQrBuEf8cZV4Dvg
  ```
  Never push.
- Branch: `feat/voice-dubbing`, worktree `/home/undead/projects/actual-chat-voice-dubbing`. Issue #4496 is linked (`branch.feat/voice-dubbing.issue`); move it to In Progress on the board with `/track-issue in-progress` once Task 1 is committed.
- Soniox integration tests self-skip when `CoreSettings__SonioxKey` is unset; they are the phase-0 spikes and must be run once with the key before Task 8.

## Reuse

Existing abstractions used (do not re-implement): `SonioxTranscriber`'s WebSocket shape and its `Sender` (extracted to `SonioxSocketSender`), `TranscriberHelper.WhenPushAndRead`, `CoreServerSettings.SonioxKey`, `SonioxLanguage.ToSoniox`, `Constants.Transcription.Soniox`, OpusSharp `OpusEncoder` (as `App.Maui/Services/Recording/OpusAudioCodec.cs` uses it), `OpusToPcmDecoder` (tests), `StreamStore<AudioFrame>` + `AsyncMemoizer` + `IAsyncEnumerable.Memoize`, `ActualOpusStreamHeader`, `AudioStreamingBackend.GetTranscript`'s lazy-start pattern and `_translatingStreams`, `FuncWorker`, `HostLifetime.CreateStopTokenSource`, `Transcript`/`TranscriptDiff` folding (`TranscriptFolder`), `FoldingAsyncMemoizer.Fold`, `ListeningStreamMuxer`, `LiveAudioStreams.GetStream` (local/remote routing, permissions via `GetChatId` → `BaseStreamId`), `ResilientStream.Break` re-subscribe path, `TranslationUI.IsEnabled/GetTranslationLanguage`, `UserSettingsUI` accessors, `LanguageUI.Settings/UpdateSettings`, `HeaderButton`, `TileItem`/`Toggle`, the `ListeningStreamMuxerTest.MuxerHarness`, `TranscriptSnapshotTest` as the backend integration-test template.

New shared-worthy components and where they go: `ISpeechSynthesizer` + `SpeechSynthesisOptions` → `Transcription.Contracts` (provider-agnostic, next to `ITranscriber`); `OpusFramePump` → `Transcription.Service` (the only server project with OpusSharp; promote to a shared audio project if a second consumer appears); `DubStabilizer` → `Streaming.Service/Audio` (pure, depends only on `Transcript`). Nothing goes to Core.

## File Structure

| File | Responsibility |
|---|---|
| `src/dotnet/Transcription.Contracts/ISpeechSynthesizer.cs` (new) | Contract: text chunks in → paced 20 ms Opus `AudioFrame`s out |
| `src/dotnet/Transcription.Service/Synthesis/OpusFramePump.cs` (new) | PCM → Opus frames at wall-clock pace, silence fill |
| `src/dotnet/Transcription.Service/Transcribers/SonioxSocketSender.cs` (new) | The send lock extracted from `SonioxTranscriber` |
| `src/dotnet/Transcription.Service/Transcribers/SonioxTtsClient.cs` (new) | `tts-rt` WebSocket session: streams, rollover, keepalive |
| `src/dotnet/Transcription.Service/Transcribers/SonioxModels.cs` | + `SonioxTtsResponse` |
| `src/dotnet/Transcription.Service/Synthesis/SonioxSpeechSynthesizer.cs` (new) | Composes client + pump |
| `src/dotnet/Transcription.Service/Synthesis/FakeSpeechSynthesizer.cs` (new) | Silence per character, for tests |
| `src/dotnet/Transcription.Service/Module/TranscriptionSettings.cs`, `TranscriptionServiceModule.cs` | `SonioxTtsVoice`, registrations |
| `src/dotnet/Api/Constants.cs`, `Constants.Audio.cs` | TTS + dub constants |
| `src/dotnet/Api/Live/LiveAudioStreamInfo.cs` | + `Languages`, `DubLanguage`, `MaySpeak` |
| `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs` | fills `Languages`, remembers author |
| `src/dotnet/Streaming.Service/Audio/DubStabilizer.cs` (new) | Stable-suffix extraction + dub/no-dub decision |
| `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.Dubbing.cs` (new) | Lazy dub start, worker, chain, expiry |
| `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.cs` | `GetAudio` hook, `GetOrStartTranslation`, author map |
| `src/dotnet/Streaming.Service/Services/ListeningStreamMuxer.cs` | `dubLanguage`, substitution, fallback, merge exemption |
| `src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs`, `Streaming.Service/Services/LiveAudioStreams.cs` | 5-arg `GetListeningStream` |
| `src/dotnet/Api/Users/UserLanguageSettings.cs`, `Api/Chat/StoredSettings/ChatUserSettings.cs` | `IsTranslatedVoiceEnabled` |
| `src/dotnet/UI.Blazor.App/Services/TranslationUI/TranslationUI.cs` | `GetDubLanguage`, `SetTranslatedVoice` |
| `src/dotnet/UI.Blazor.App/Services/Audio/ListeningStreamProcessor.cs`, `Services/Playback/ChatListeningPlayer.cs` | pass dub language, re-subscribe on change |
| `src/dotnet/UI.Blazor.App/Components/Settings/TranscriptionSettings.razor` | listener toggle |
| `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/LiveConversationHeaderView.razor`, `LiveConversationHeaderState.cs`, `conversation.css` | per-chat chip |
| `src/dotnet/Localization/Resources/Strings.*.json`, `LocalizedStringsLocalizerExt.cs` | 4 keys |
| `docs/live-audio/12-dubbing.md` (new), `docs/live-audio/README.md` | pipeline doc |
| Tests: `tests/Transcription.UnitTests/OpusFramePumpTest.cs`, `tests/Transcription.IntegrationTests/SonioxTtsClientTest.cs`, `SonioxSpeechSynthesizerTest.cs`, `tests/Streaming.UnitTests/DubStabilizerTest.cs`, `ListeningStreamMuxerTest.cs`, `StreamingSerializationTest.cs`, `tests/Streaming.IntegrationTests/DubbingTest.cs` | |

---

### Task 1: `ISpeechSynthesizer` contract and constants

**Files:**
- Create: `src/dotnet/Transcription.Contracts/ISpeechSynthesizer.cs`
- Modify: `src/dotnet/Api/Constants.cs` (inside `Transcription.Soniox`, after `SilentSuffixDuration`)
- Modify: `src/dotnet/Api/Constants.Audio.cs` (inside `Audio`, next to `MaxBeginsAtDrift`)

**Interfaces:**
- Produces: `ISpeechSynthesizer.Synthesize(string streamId, ChannelReader<string> text, SpeechSynthesisOptions options, ChannelWriter<AudioFrame> output, CancellationToken ct)`; `SpeechSynthesisOptions(Language Language, string? VoiceId = null)`; `Constants.Transcription.Soniox.TtsKeepAlivePeriod` (15 s), `TtsMaxStreamDuration` (90 s); `Constants.Audio.DubWaitTimeout` (5 s).

- [ ] **Step 1: Write the contract**

```csharp
using ActualChat.Audio;

namespace ActualChat.Transcription;

public sealed record SpeechSynthesisOptions(Language Language, string? VoiceId = null);

/// <summary>
/// Speaks a stream of text chunks as 20 ms Opus <see cref="AudioFrame"/>s (48 kHz mono) emitted at
/// wall-clock pace with contiguous offsets from zero; gaps between chunks come out as silence.
/// </summary>
public interface ISpeechSynthesizer
{
    Task Synthesize(
        string streamId,
        ChannelReader<string> text,
        SpeechSynthesisOptions options,
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Add the constants**

In `Constants.cs`, `Transcription.Soniox` block, after `SilentSuffixDuration`:

```csharp
            // TTS: the server closes an idle connection after 20-30s without a message
            public static readonly TimeSpan TtsKeepAlivePeriod = TimeSpan.FromSeconds(15);
            // TTS caps one stream at 2 minutes of generated audio; text in flight lands in the gap
            public static readonly TimeSpan TtsMaxStreamDuration = TimeSpan.FromSeconds(90);
```

In `Constants.Audio.cs`, next to `MaxBeginsAtDrift`:

```csharp
        // How long a dubbing listener's muxer holds a speaker's original before serving it undubbed
        public static readonly TimeSpan DubWaitTimeout = TimeSpan.FromSeconds(5);
```

- [ ] **Step 3: Build**

Run: `dotnet build src/dotnet/Transcription.Contracts/Transcription.Contracts.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/Transcription.Contracts/ISpeechSynthesizer.cs src/dotnet/Api/Constants.cs src/dotnet/Api/Constants.Audio.cs
git commit -m "feat(dubbing): ISpeechSynthesizer contract and TTS constants"
```

Then run `/track-issue in-progress`.

---

### Task 2: `OpusFramePump`

**Files:**
- Create: `src/dotnet/Transcription.Service/Synthesis/OpusFramePump.cs`
- Test: `tests/Transcription.UnitTests/OpusFramePumpTest.cs`

**Interfaces:**
- Produces: `OpusFramePump(MomentClock clock)`, `Task Run(ChannelReader<byte[]> pcm, ChannelWriter<AudioFrame> output, CancellationToken ct)`, `const int FrameLength = 960`, `const int FrameByteLength = 1920`. Input is 48 kHz mono `pcm_s16le`; output frames have `Offset = 20ms × index`, `Duration = 20ms`; `output` is completed (with the error, if any) when `Run` ends.

- [ ] **Step 1: Write the failing tests**

```csharp
using ActualChat.Audio;

namespace ActualChat.Transcription.UnitTests;

public class OpusFramePumpTest
{
    [Fact]
    public async Task PumpShouldEmitContiguousFramesForCompleteInput()
    {
        // arrange
        using var pump = new OpusFramePump(MomentClockSet.Default.CpuClock);
        var pcm = Channel.CreateUnbounded<byte[]>();
        var frames = Channel.CreateUnbounded<AudioFrame>();
        pcm.Writer.TryWrite(new byte[OpusFramePump.FrameByteLength * 3]);
        pcm.Writer.Complete();

        // act
        await pump.Run(pcm.Reader, frames.Writer, CancellationToken.None);
        var result = await frames.Reader.ReadAllAsync().ToListAsync();

        // assert
        result.Should().HaveCount(3);
        result.Select(f => f.Offset).Should().Equal(
            TimeSpan.Zero, Constants.Audio.OpusFrameDuration, Constants.Audio.OpusFrameDuration * 2);
        result.Should().OnlyContain(f => f.Data.Length > 0);
        result.Should().OnlyContain(f => f.Duration == Constants.Audio.OpusFrameDuration);
    }

    [Fact]
    public async Task PumpShouldFillGapsWithSilence()
    {
        // arrange
        using var pump = new OpusFramePump(MomentClockSet.Default.CpuClock);
        var pcm = Channel.CreateUnbounded<byte[]>();
        var frames = Channel.CreateUnbounded<AudioFrame>();
        var runTask = pump.Run(pcm.Reader, frames.Writer, CancellationToken.None);

        // act
        await Task.Delay(200);
        pcm.Writer.TryWrite(new byte[OpusFramePump.FrameByteLength]);
        pcm.Writer.Complete();
        await runTask;
        var result = await frames.Reader.ReadAllAsync().ToListAsync();

        // assert
        result.Count.Should().BeGreaterThanOrEqualTo(6,
            "200ms of waiting is 10 silence frames minus scheduling slack, plus the real one");
        for (var i = 0; i < result.Count; i++)
            result[i].Offset.Should().Be(Constants.Audio.OpusFrameDuration * i);
    }

    [Fact]
    public async Task PumpShouldPadTheTailFrame()
    {
        // arrange
        using var pump = new OpusFramePump(MomentClockSet.Default.CpuClock);
        var pcm = Channel.CreateUnbounded<byte[]>();
        var frames = Channel.CreateUnbounded<AudioFrame>();
        pcm.Writer.TryWrite(new byte[OpusFramePump.FrameByteLength + OpusFramePump.FrameByteLength / 2]);
        pcm.Writer.Complete();

        // act
        await pump.Run(pcm.Reader, frames.Writer, CancellationToken.None);
        var result = await frames.Reader.ReadAllAsync().ToListAsync();

        // assert
        result.Should().HaveCount(2, "the half frame is padded with silence rather than dropped");
    }

    [Fact]
    public async Task PumpShouldPropagateTheProducersError()
    {
        // arrange
        using var pump = new OpusFramePump(MomentClockSet.Default.CpuClock);
        var pcm = Channel.CreateUnbounded<byte[]>();
        var frames = Channel.CreateUnbounded<AudioFrame>();
        pcm.Writer.Complete(new InvalidOperationException("tts died"));

        // act
        var act = () => pump.Run(pcm.Reader, frames.Writer, CancellationToken.None);

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("tts died");
        var readAll = () => frames.Reader.ReadAllAsync().ToListAsync().AsTask();
        await readAll.Should().ThrowAsync<InvalidOperationException>("the output carries the same error");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Transcription.UnitTests/Transcription.UnitTests.csproj --filter "FullyQualifiedName~OpusFramePumpTest"`
Expected: build error — `OpusFramePump` does not exist.

- [ ] **Step 3: Write the pump**

```csharp
using System.Runtime.InteropServices;
using ActualChat.Audio;
using OpusSharp.Core;

namespace ActualChat.Transcription;

/// <summary>
/// Encodes 48 kHz mono PCM into 20 ms Opus <see cref="AudioFrame"/>s emitted at wall-clock pace;
/// every gap in the input becomes encoded silence, so offsets stay contiguous from zero.
/// </summary>
public sealed class OpusFramePump : IDisposable
{
    public const int SampleRate = 48_000;
    public const int FrameLength = SampleRate / 1000 * Constants.Audio.OpusFrameDurationMs;
    public const int FrameByteLength = FrameLength * sizeof(short);
    private const int MaxPacketLength = 4096;

    private readonly OpusEncoder _encoder;
    private readonly short[] _pcm = new short[FrameLength];
    private readonly byte[] _packet = new byte[MaxPacketLength];
    private readonly PcmBuffer _buffer = new();

    private MomentClock Clock { get; }

    public OpusFramePump(MomentClock clock)
    {
        Clock = clock;
        _encoder = new OpusEncoder(SampleRate, Constants.Audio.Channels, OpusPredefinedValues.OPUS_APPLICATION_VOIP);
        _encoder.SetBitRate(Constants.Audio.Bitrate);
        _encoder.SetVbr(true);
        _encoder.SetSignal(OpusPredefinedValues.OPUS_SIGNAL_VOICE);
    }

    public void Dispose()
        => _encoder.Dispose();

    public async Task Run(
        ChannelReader<byte[]> pcm,
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            var startedAt = Clock.Now;
            var frameIndex = 0;
            while (true) {
                // Completion is read before the drain, so a chunk written right before Complete()
                // is always drained by the pass that observes the completion.
                var isInputCompleted = pcm.Completion.IsCompleted;
                while (pcm.TryRead(out var chunk))
                    _buffer.Append(chunk);
                if (isInputCompleted && _buffer.Length == 0)
                    break;

                if (!_buffer.TryTake(_pcm, mustPadTail: isInputCompleted))
                    Array.Clear(_pcm);
                var frame = Encode(frameIndex++);
                var delay = startedAt + Constants.Audio.OpusFrameDuration * frameIndex - Clock.Now;
                if (delay > TimeSpan.Zero)
                    await Clock.Delay(delay, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            }
            await pcm.Completion.ConfigureAwait(false);
        }
        catch (Exception e) {
            error = e;
            throw;
        }
        finally {
            output.TryComplete(error);
        }
    }

    // Private methods

    private AudioFrame Encode(int frameIndex)
    {
        var length = _encoder.Encode(_pcm, FrameLength, _packet, MaxPacketLength);
        if (length <= 0)
            throw StandardError.Internal($"Opus encoder returned {length}.");

        return new AudioFrame {
            Data = _packet.AsSpan(0, length).ToArray(),
            Offset = Constants.Audio.OpusFrameDuration * frameIndex,
        };
    }

    // Nested types

    private sealed class PcmBuffer
    {
        private byte[] _bytes = new byte[FrameByteLength * 16];
        private int _start;
        private int _end;

        public int Length => _end - _start;

        public void Append(byte[] chunk)
        {
            if (_end + chunk.Length > _bytes.Length) {
                var length = Length;
                if (length + chunk.Length > _bytes.Length)
                    Array.Resize(ref _bytes, Math.Max(_bytes.Length * 2, length + chunk.Length));
                Buffer.BlockCopy(_bytes, _start, _bytes, 0, length);
                _start = 0;
                _end = length;
            }
            Buffer.BlockCopy(chunk, 0, _bytes, _end, chunk.Length);
            _end += chunk.Length;
        }

        public bool TryTake(short[] frame, bool mustPadTail)
        {
            var length = Length;
            if (length < FrameByteLength && !(mustPadTail && length > 0))
                return false;

            var takeLength = Math.Min(length, FrameByteLength);
            var takeSampleCount = takeLength / sizeof(short);
            MemoryMarshal.Cast<byte, short>(_bytes.AsSpan(_start, takeLength)).CopyTo(frame);
            Array.Clear(frame, takeSampleCount, frame.Length - takeSampleCount);
            _start += takeLength;
            if (_start == _end)
                _start = _end = 0;
            return true;
        }
    }
}
```

If `OpusEncoder.Encode` has no `(short[], int, byte[], int)` overload in the referenced OpusSharp version, use the `Span<short>`/`Span<byte>` overload exactly as `App.Maui/Services/Recording/OpusAudioCodec.cs:78` does.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Transcription.UnitTests/Transcription.UnitTests.csproj --filter "FullyQualifiedName~OpusFramePumpTest"`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Transcription.Service/Synthesis/OpusFramePump.cs tests/Transcription.UnitTests/OpusFramePumpTest.cs
git commit -m "feat(dubbing): OpusFramePump - paced, silence-filled PCM to Opus frames"
```

---

### Task 3: Extract `SonioxSocketSender`

**Files:**
- Create: `src/dotnet/Transcription.Service/Transcribers/SonioxSocketSender.cs`
- Modify: `src/dotnet/Transcription.Service/Transcribers/SonioxTranscriber.cs` (remove nested `Sender`, use the new class)

**Interfaces:**
- Produces: `internal sealed class SonioxSocketSender(ClientWebSocket webSocket, MomentClock clock) : IDisposable` with `Moment LastSendAt` and `Task Send(ReadOnlyMemory<byte> data, WebSocketMessageType messageType, CancellationToken ct)` — identical to today's nested `SonioxTranscriber.Sender`.

- [ ] **Step 1: Move the class**

Create `SonioxSocketSender.cs` with the body of the nested `Sender` class from `SonioxTranscriber.cs` (lines "// ClientWebSocket allows just one send at a time…" through the end of the class), renamed to `SonioxSocketSender`, `internal sealed`, `namespace ActualChat.Transcription;`, `using System.Net.WebSockets;`. Keep the two-line comment explaining why it exists, moved inside the class doc as a `// ` comment above the type (per the style guide's placement order: regular comment, blank line, type).

In `SonioxTranscriber.cs`: delete the nested class and its `// Nested types` separator, and replace `using var sender = new Sender(webSocket, Clocks.CpuClock);` with `using var sender = new SonioxSocketSender(webSocket, Clocks.CpuClock);`; change the parameter types `Sender sender` → `SonioxSocketSender sender` in `SendConfig`, `KeepAlive`, `PushAudio`.

- [ ] **Step 2: Build and run the existing Soniox tests**

Run: `dotnet build src/dotnet/Transcription.Service/Transcription.Service.csproj`
Expected: Build succeeded.
Run: `dotnet test tests/Transcription.IntegrationTests/Transcription.IntegrationTests.csproj --filter "FullyQualifiedName~SonioxTranscriberTest.StreamingTranscribeWorks"`
Expected: passes (or self-skips without the key) — no behavior change.

- [ ] **Step 3: Commit**

```bash
git add src/dotnet/Transcription.Service/Transcribers/SonioxSocketSender.cs src/dotnet/Transcription.Service/Transcribers/SonioxTranscriber.cs
git commit -m "refactor(transcription): extract SonioxSocketSender for reuse by TTS"
```

---

### Task 4: `SonioxTtsClient` (spike #1 — verified against the live API)

**Files:**
- Create: `src/dotnet/Transcription.Service/Transcribers/SonioxTtsClient.cs`
- Modify: `src/dotnet/Transcription.Service/Transcribers/SonioxModels.cs` (+ `SonioxTtsResponse`)
- Test: `tests/Transcription.IntegrationTests/SonioxTtsClientTest.cs`

**Interfaces:**
- Consumes: `SonioxSocketSender` (Task 3), `TranscriberHelper.WhenPushAndRead`, `Constants.Transcription.Soniox.TtsKeepAlivePeriod/TtsMaxStreamDuration` (Task 1).
- Produces: `SonioxTtsClient(IServiceProvider services, SonioxTtsClient.Options? options = null)`; `record Options { TimeSpan MaxStreamDuration }`; `int StreamCount { get; }`; `Task Run(string sessionId, string language, string voice, ChannelReader<string> text, ChannelWriter<byte[]> pcm, CancellationToken ct)`. `pcm` receives raw `pcm_s16le` 48 kHz mono chunks and is completed (with the error, if any) when `Run` ends.

Protocol (from https://soniox.com/docs/api-reference/tts/websocket-api): endpoint `wss://tts-rt.soniox.com/tts-websocket`; per stream a config `{api_key, model:"tts-rt-v2", language, voice, audio_format:"pcm_s16le", sample_rate:48000, stream_id}` (send within ~10 s of connecting); text messages `{text, text_end, stream_id}`; responses `{audio (base64), audio_end, stream_id}`, `{terminated:true, stream_id}`, errors `{error_code, error_type, error_message, stream_id}`; keepalive `{"keep_alive":true}` every 20–30 s idle; max 2 min of audio per stream, 5 streams per connection, 5000 chars per text message; a client-side `{cancel:true, stream_id}` exists but is not used.

- [ ] **Step 1: Write the failing integration tests**

```csharp
using ActualChat.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public class SonioxTtsClientTest(ITestOutputHelper @out, ILogger<SonioxTtsClientTest> log)
    : TranscriberTestBase(@out, log)
{
    private const int BytesPerSecond = 48_000 * sizeof(short);

    [Fact]
    public async Task TtsShouldReturnPcmForStreamedText()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var client = new SonioxTtsClient(services);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        text.Writer.TryWrite("Hello there, this is a test of the dubbing pipeline.");
        text.Writer.TryWrite(" And here is one more sentence.");
        text.Writer.Complete();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // act
        await client.Run("test", "en", "Adrian", text.Reader, pcm.Writer, cts.Token);
        var chunks = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        var totalBytes = chunks.Sum(c => (long)c.Length);
        WriteLine($"{chunks.Count} chunks, {totalBytes / (double)BytesPerSecond:F1}s of audio");
        totalBytes.Should().BeGreaterThan(BytesPerSecond, "two sentences are well over a second of speech");
        client.StreamCount.Should().Be(1);
    }

    [Fact]
    public async Task TtsShouldRollOverPastMaxStreamDuration()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var client = new SonioxTtsClient(services, new SonioxTtsClient.Options {
            MaxStreamDuration = TimeSpan.FromSeconds(1),
        });
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // act
        var runTask = client.Run("test", "en", "Adrian", text.Reader, pcm.Writer, cts.Token);
        var drainTask = pcm.Reader.ReadAllAsync().Select(c => (long)c.Length).SumAsync().AsTask();
        for (var i = 0; i < 3; i++) {
            text.Writer.TryWrite($"Sentence number {i + 1} is long enough to take a couple of seconds to say out loud.");
            // Rollover is decided on the audio already generated, so give each sentence time to land
            await Task.Delay(TimeSpan.FromSeconds(4));
        }
        text.Writer.Complete();
        await runTask;
        var totalBytes = await drainTask;

        // assert
        WriteLine($"{client.StreamCount} streams, {totalBytes / (double)BytesPerSecond:F1}s of audio");
        client.StreamCount.Should().BeGreaterThanOrEqualTo(2,
            "every sentence exceeds the 1s cap, so the second one must open a new stream");
        totalBytes.Should().BeGreaterThan(3 * BytesPerSecond);
    }

    private IServiceProvider CreateServices()
    {
        IConfiguration configuration = new ConfigurationManager {
            Sources = { new EnvironmentVariablesConfigurationSource() },
        };
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(_ => configuration)
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton(_ => configuration.Settings<CoreServerSettings>(nameof(CoreSettings)))
            .AddSoniox()
            .AddTestLogging(Out)
            .BuildServiceProvider();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Transcription.IntegrationTests/Transcription.IntegrationTests.csproj --filter "FullyQualifiedName~SonioxTtsClientTest"`
Expected: build error — `SonioxTtsClient` does not exist.

- [ ] **Step 3: Add the response model**

Append to `SonioxModels.cs`:

```csharp
public sealed class SonioxTtsResponse
{
    [JsonPropertyName("stream_id")] public string? StreamId { get; set; }
    [JsonPropertyName("audio")] public string? Audio { get; set; }
    [JsonPropertyName("audio_end")] public bool AudioEnd { get; set; }
    [JsonPropertyName("terminated")] public bool Terminated { get; set; }
    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
}
```

- [ ] **Step 4: Write the client**

```csharp
using System.Net.WebSockets;
using System.Text;
using ActualChat.Module;
using static ActualChat.Constants.Transcription.Soniox;

namespace ActualChat.Transcription;

/// <summary>
/// One real-time session of Soniox's <c>tts-rt</c> WebSocket API: text chunks in, 48 kHz PCM out.
/// A session spans several Soniox streams, each capped at <see cref="Options.MaxStreamDuration"/>.
/// </summary>
public sealed class SonioxTtsClient(IServiceProvider services, SonioxTtsClient.Options? options = null)
{
    public sealed record Options
    {
        public TimeSpan MaxStreamDuration { get; init; } = TtsMaxStreamDuration;
    }

    private const string Url = "wss://tts-rt.soniox.com/tts-websocket";
    private const string Model = "tts-rt-v2";
    private const string PcmFormat = "pcm_s16le";
    private const int SampleRate = 48_000;
    private const int BytesPerSecond = SampleRate * sizeof(short);
    private static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly byte[] KeepAlivePayload = """{"keep_alive":true}"""u8.ToArray();

    private readonly ConcurrentDictionary<string, StreamState> _streams = new();
    private string? _finalStreamId;

    private CoreServerSettings CoreServerSettings { get; } = services.GetRequiredService<CoreServerSettings>();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log { get; } = services.LogFor<SonioxTtsClient>();

    public Options Settings { get; } = options ?? new();
    public int StreamCount { get; private set; }

    public async Task Run(
        string sessionId,
        string language,
        string voice,
        ChannelReader<string> text,
        ChannelWriter<byte[]> pcm,
        CancellationToken cancellationToken)
    {
        var apiKey = CoreServerSettings.SonioxKey;
        if (apiKey.IsNullOrEmpty())
            throw StandardError.Configuration("CoreSettings:SonioxKey is not set.");

        Exception? error = null;
        using var webSocket = new ClientWebSocket();
        using var sender = new SonioxSocketSender(webSocket, Clocks.CpuClock);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? keepAliveTask = null;
        try {
            await webSocket.ConnectAsync(new Uri(Url), cancellationToken).ConfigureAwait(false);
            var config = new StreamConfig(sessionId, apiKey, language, voice);
            keepAliveTask = KeepAlive(sender, cts.Token);
            await TranscriberHelper.WhenPushAndRead(
                    PushText(sender, config, text, cts.Token),
                    ReadAudio(webSocket, sessionId, pcm, cts.Token),
                    cts)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            error = e;
            if (e is not OperationCanceledException)
                Log.LogError(e, "Soniox TTS failed for #{SessionId}", sessionId);
            throw;
        }
        finally {
            await cts.CancelAsync().ConfigureAwait(false);
            if (keepAliveTask != null)
                await keepAliveTask.SilentAwait(false);
            pcm.TryComplete(error);
        }
    }

    // Private methods

    private async Task PushText(
        SonioxSocketSender sender,
        StreamConfig config,
        ChannelReader<string> text,
        CancellationToken cancellationToken)
    {
        // The first stream opens before any text exists: the config has to reach the server within
        // ~10s of connecting, and the first translated sentence can take longer than that.
        var streamId = await StartStream(sender, config, cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in text.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
            if (chunk.IsNullOrWhiteSpace())
                continue;

            if (_streams[streamId].GeneratedDuration >= Settings.MaxStreamDuration) {
                await EndStream(sender, streamId, cancellationToken).ConfigureAwait(false);
                streamId = await StartStream(sender, config, cancellationToken).ConfigureAwait(false);
            }
            await Send(sender, new { text = chunk, text_end = false, stream_id = streamId }, cancellationToken)
                .ConfigureAwait(false);
        }

        Volatile.Write(ref _finalStreamId, streamId);
        await EndStream(sender, streamId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> StartStream(
        SonioxSocketSender sender,
        StreamConfig config,
        CancellationToken cancellationToken)
    {
        var streamId = $"{config.SessionId}-{++StreamCount}";
        _streams[streamId] = new StreamState();
        var message = new Dictionary<string, object?> {
            ["api_key"] = config.ApiKey,
            ["model"] = Model,
            ["language"] = config.Language,
            ["voice"] = config.Voice,
            ["audio_format"] = PcmFormat,
            ["sample_rate"] = SampleRate,
            ["stream_id"] = streamId,
        };
        await Send(sender, message, cancellationToken).ConfigureAwait(false);
        return streamId;
    }

    private async Task EndStream(SonioxSocketSender sender, string streamId, CancellationToken cancellationToken)
    {
        await Send(sender, new { text = "", text_end = true, stream_id = streamId }, cancellationToken)
            .ConfigureAwait(false);
        await _streams[streamId].WhenTerminated.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Task Send(SonioxSocketSender sender, object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        return sender.Send(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, cancellationToken);
    }

    private async Task KeepAlive(SonioxSocketSender sender, CancellationToken cancellationToken)
    {
        var clock = Clocks.CpuClock;
        while (true) {
            var delay = TtsKeepAlivePeriod - (clock.Now - sender.LastSendAt);
            if (delay > TimeSpan.Zero) {
                await clock.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            await sender.Send(KeepAlivePayload, WebSocketMessageType.Text, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReadAudio(
        ClientWebSocket webSocket,
        string sessionId,
        ChannelWriter<byte[]> pcm,
        CancellationToken cancellationToken)
    {
        var buffer = new ArraySegment<byte>(new byte[64 * 1024]);
        var message = new StringBuilder();
        while (webSocket.State == WebSocketState.Open) {
            message.Clear();
            WebSocketReceiveResult result;
            do {
                result = await webSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;

                message.Append(Encoding.UTF8.GetString(buffer.Array!, 0, result.Count));
            } while (!result.EndOfMessage);

            var response = JsonSerializer.Deserialize<SonioxTtsResponse>(message.ToString(), JsonOptions);
            if (response == null)
                continue;
            if (response.ErrorCode is { } errorCode)
                throw StandardError.External(
                    $"Soniox TTS error {errorCode} for #{sessionId}: {response.ErrorMessage}");

            var streamId = response.StreamId ?? "";
            if (!response.Audio.IsNullOrEmpty()) {
                var bytes = Convert.FromBase64String(response.Audio);
                if (_streams.TryGetValue(streamId, out var state))
                    state.GeneratedDuration += TimeSpan.FromSeconds(bytes.Length / (double)BytesPerSecond);
                await pcm.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
            if (response.Terminated) {
                if (_streams.TryGetValue(streamId, out var state))
                    state.WhenTerminated.TrySetResult();
                if (streamId == Volatile.Read(ref _finalStreamId))
                    return;
            }
        }
    }

    // Nested types

    private sealed record StreamConfig(string SessionId, string ApiKey, string Language, string Voice);

    private sealed class StreamState
    {
        public TaskCompletionSource WhenTerminated { get; } = TaskCompletionSourceExt.New();
        public TimeSpan GeneratedDuration { get; set; }
    }
}
```

- [ ] **Step 5: Run the spike tests against the live API**

Run (with the key exported, e.g. `export CoreSettings__SonioxKey=…` from your `docs-internal/set-local-env` setup):
`dotnet test tests/Transcription.IntegrationTests/Transcription.IntegrationTests.csproj --filter "FullyQualifiedName~SonioxTtsClientTest"`
Expected: 2 passed, output lines showing seconds of audio and `StreamCount >= 2` for the rollover test.

If the server rejects `{"text":"","text_end":true}`, send `" "` instead of `""` in `EndStream` and re-run. If `"Adrian"` is not a valid voice name, list voices in the Soniox console and change the literal in the tests and in `TranscriptionSettings.SonioxTtsVoice` (Task 5).

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Transcription.Service/Transcribers/SonioxTtsClient.cs src/dotnet/Transcription.Service/Transcribers/SonioxModels.cs tests/Transcription.IntegrationTests/SonioxTtsClientTest.cs
git commit -m "feat(dubbing): SonioxTtsClient - tts-rt-v2 WebSocket session with rollover"
```

---

### Task 5: `SonioxSpeechSynthesizer`, `FakeSpeechSynthesizer`, registration (spike #2)

**Files:**
- Create: `src/dotnet/Transcription.Service/Synthesis/SonioxSpeechSynthesizer.cs`
- Create: `src/dotnet/Transcription.Service/Synthesis/FakeSpeechSynthesizer.cs`
- Modify: `src/dotnet/Transcription.Service/Module/TranscriptionSettings.cs`
- Modify: `src/dotnet/Transcription.Service/Module/TranscriptionServiceModule.cs`
- Test: `tests/Transcription.IntegrationTests/SonioxSpeechSynthesizerTest.cs`

**Interfaces:**
- Consumes: `ISpeechSynthesizer` (Task 1), `OpusFramePump` (Task 2), `SonioxTtsClient` (Task 4), `SonioxLanguage.ToSoniox`, `OpusToPcmDecoder` (test only).
- Produces: `SonioxSpeechSynthesizer(IServiceProvider)`, `FakeSpeechSynthesizer(IServiceProvider)` — both `ISpeechSynthesizer`; `TranscriptionSettings.SonioxTtsVoice` (default `"Adrian"`). DI: `ISpeechSynthesizer` is registered iff the Soniox key is set (Soniox) or `UseFakeTranscriber` (fake); resolve it with `GetService` (optional).

- [ ] **Step 1: Write the failing integration test**

```csharp
using ActualChat.Audio;
using ActualChat.Module;
using ActualChat.Transcription.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public class SonioxSpeechSynthesizerTest(ITestOutputHelper @out, ILogger<SonioxSpeechSynthesizerTest> log)
    : TranscriberTestBase(@out, log)
{
    [Fact]
    public async Task SynthesizerShouldProduceAudibleOpusFrames()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var synthesizer = new SonioxSpeechSynthesizer(services);
        var text = Channel.CreateUnbounded<string>();
        var frames = Channel.CreateUnbounded<AudioFrame>();
        text.Writer.TryWrite("Привет, это проверка синтеза речи.");
        text.Writer.Complete();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // act
        await synthesizer.Synthesize(
            "test", text.Reader, new SpeechSynthesisOptions(Languages.Russian), frames.Writer, cts.Token);
        var result = await frames.Reader.ReadAllAsync().ToListAsync();

        // assert
        WriteLine($"{result.Count} frames = {result.Count * Constants.Audio.OpusFrameDurationMs / 1000.0:F1}s");
        result.Count.Should().BeGreaterThan(50, "a sentence is more than a second of 20ms frames");
        for (var i = 0; i < result.Count; i++)
            result[i].Offset.Should().Be(Constants.Audio.OpusFrameDuration * i);

        using var decoder = new OpusToPcmDecoder();
        var loudFrameCount = result.Count(f => Rms(decoder.Decode(f.Data.Span)) > 200);
        loudFrameCount.Should().BeGreaterThan(10, "speech must decode to something well above silence");
    }

    private static double Rms(byte[] pcm)
    {
        if (pcm.Length == 0)
            return 0;

        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(pcm);
        var sum = 0.0;
        foreach (var sample in samples)
            sum += (double)sample * sample;
        return Math.Sqrt(sum / samples.Length);
    }

    private IServiceProvider CreateServices()
    {
        IConfiguration configuration = new ConfigurationManager {
            Sources = { new EnvironmentVariablesConfigurationSource() },
        };
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(_ => configuration)
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton(_ => configuration.Settings<CoreServerSettings>(nameof(CoreSettings)))
            .AddSingleton(new TranscriptionSettings())
            .AddSoniox()
            .AddTestLogging(Out)
            .BuildServiceProvider();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Transcription.IntegrationTests/Transcription.IntegrationTests.csproj --filter "FullyQualifiedName~SonioxSpeechSynthesizerTest"`
Expected: build error — `SonioxSpeechSynthesizer` does not exist.

- [ ] **Step 3: Add the setting**

In `TranscriptionSettings`:

```csharp
    // The built-in Soniox voice used for speakers without a cloned voice
    public string SonioxTtsVoice { get; set; } = "Adrian";
```

- [ ] **Step 4: Write both synthesizers**

`SonioxSpeechSynthesizer.cs`:

```csharp
using ActualChat.Audio;
using ActualChat.Transcription.Module;

namespace ActualChat.Transcription;

public sealed class SonioxSpeechSynthesizer(IServiceProvider services) : ISpeechSynthesizer
{
    private IServiceProvider Services { get; } = services;
    private TranscriptionSettings Settings { get; } = services.GetRequiredService<TranscriptionSettings>();
    private MomentClockSet Clocks { get; } = services.Clocks();

    public async Task Synthesize(
        string streamId,
        ChannelReader<string> text,
        SpeechSynthesisOptions options,
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken = default)
    {
        var pcm = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
        });
        using var pump = new OpusFramePump(Clocks.CpuClock);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var client = new SonioxTtsClient(Services);
        var voice = options.VoiceId ?? Settings.SonioxTtsVoice;
        await TranscriberHelper.WhenPushAndRead(
                client.Run(streamId, options.Language.ToSoniox(), voice, text, pcm.Writer, cts.Token),
                pump.Run(pcm.Reader, output, cts.Token),
                cts)
            .ConfigureAwait(false);
    }
}
```

`FakeSpeechSynthesizer.cs`:

```csharp
using ActualChat.Audio;

namespace ActualChat.Transcription;

/// <summary>
/// Speaks one 20 ms frame of silence per four characters, so tests get real pacing without a provider.
/// </summary>
public sealed class FakeSpeechSynthesizer(IServiceProvider services) : ISpeechSynthesizer
{
    private MomentClockSet Clocks { get; } = services.Clocks();

    public async Task Synthesize(
        string streamId,
        ChannelReader<string> text,
        SpeechSynthesisOptions options,
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken = default)
    {
        var pcm = Channel.CreateUnbounded<byte[]>();
        using var pump = new OpusFramePump(Clocks.CpuClock);
        var pumpTask = pump.Run(pcm.Reader, output, cancellationToken);
        try {
            await foreach (var chunk in text.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                var frameCount = Math.Max(1, chunk.Length / 4);
                pcm.Writer.TryWrite(new byte[OpusFramePump.FrameByteLength * frameCount]);
            }
        }
        finally {
            pcm.Writer.TryComplete();
        }
        await pumpTask.ConfigureAwait(false);
    }
}
```

- [ ] **Step 5: Register**

In `TranscriptionServiceModule.InjectServices`:

```csharp
        if (Settings.UseFakeTranscriber) {
            // Registered alone so the ranking can't route around it in tests.
            services.AddSingleton<ITranscriber, FakeTranscriber>();
            services.AddSingleton<ISpeechSynthesizer, FakeSpeechSynthesizer>();
            return;
        }
```

and inside `if (!coreSettings.SonioxKey.IsNullOrEmpty()) {` after the `SonioxOfflineTranscriber` line:

```csharp
            services.AddSingleton<ISpeechSynthesizer, SonioxSpeechSynthesizer>();
```

- [ ] **Step 6: Run the spike test against the live API**

Run: `dotnet test tests/Transcription.IntegrationTests/Transcription.IntegrationTests.csproj --filter "FullyQualifiedName~SonioxSpeechSynthesizerTest"`
Expected: passed; output shows > 1 s of frames and loud frames.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Transcription.Service/Synthesis tests/Transcription.IntegrationTests/SonioxSpeechSynthesizerTest.cs src/dotnet/Transcription.Service/Module
git commit -m "feat(dubbing): SonioxSpeechSynthesizer + FakeSpeechSynthesizer, DI registration"
```

---

### Task 6: `LiveAudioStreamInfo.Languages` / `DubLanguage`

**Files:**
- Modify: `src/dotnet/Api/Live/LiveAudioStreamInfo.cs`
- Modify: `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs:152-160`
- Test: `tests/Streaming.UnitTests/StreamingSerializationTest.cs`

**Interfaces:**
- Produces: `ApiArray<Language> Languages` (key 8, the speaker's candidate languages: the chat language, else what the speaker speaks), `Language? DubLanguage` (key 9, set only on a dub track's muxed start item), `bool MaySpeak(Language language)` (ISO-code match, so `en-US` matches `en-GB`).

- [ ] **Step 1: Write the failing test**

Add to `StreamingSerializationTest`:

```csharp
    [Fact]
    public void LiveStreamInfoShouldRoundTripLanguagesAndDubLanguage()
    {
        // arrange
        var info = new LiveAudioStreamInfo {
            ChatId = TestChatId,
            AuthorId = AuthorId.New(TestChatId, 5),
            StreamId = "stream-1",
            BeginsAt = new Moment(DateTime.UtcNow),
            Languages = new ApiArray<Language>([Languages.Russian, Languages.English]),
            DubLanguage = Languages.English,
        };

        // act
        var copy = info.PassThroughSerializers(Out);

        // assert
        copy.Languages.Should().Equal(Languages.Russian, Languages.English);
        copy.DubLanguage.Should().Be(Languages.English);
        copy.MaySpeak(Language.Parse("en-GB")).Should().BeTrue("English variants share an ISO code");
        copy.MaySpeak(Languages.German).Should().BeFalse();
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Streaming.UnitTests/Streaming.UnitTests.csproj --filter "FullyQualifiedName~LiveStreamInfoShouldRoundTrip"`
Expected: build error.

- [ ] **Step 3: Add the members**

After `IsTextOnly` in `LiveAudioStreamInfo`:

```csharp
    // The speaker's candidate languages: the chat language, else what they speak. A dubbing
    // listener's muxer holds the original until a dub exists only when its language isn't here.
    [DataMember(Order = 8), Key(8)]
    public ApiArray<Language> Languages { get; init; }
    // Set only on the muxed start item of a dub track; the registry's records carry null.
    [DataMember(Order = 9), Key(9)]
    public Language? DubLanguage { get; init; }

    public bool MaySpeak(Language language)
        => Languages.Any(x => x.IsoCode == language.IsoCode);
```

- [ ] **Step 4: Fill `Languages` in `ProcessAudio`**

In the `streamInfo` initializer (`AudioStreamingBackend.ProcessAudio.cs` ~line 152):

```csharp
            IsTextOnly = !mustStreamVoice,
            Languages = languages.ChatLanguage is { } chatLanguage
                ? new ApiArray<Language>([chatLanguage])
                : languages.UserSettings.ListSpoken().ToApiArray(),
```

- [ ] **Step 5: Run the test and build Streaming.Service**

Run: `dotnet test tests/Streaming.UnitTests/Streaming.UnitTests.csproj --filter "FullyQualifiedName~StreamingSerializationTest"`
Expected: all passed.
Run: `dotnet build src/dotnet/Streaming.Service/Streaming.Service.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Api/Live/LiveAudioStreamInfo.cs src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs tests/Streaming.UnitTests/StreamingSerializationTest.cs
git commit -m "feat(dubbing): LiveAudioStreamInfo carries speaker languages and DubLanguage"
```

---

### Task 7: `DubStabilizer`

**Files:**
- Create: `src/dotnet/Streaming.Service/Audio/DubStabilizer.cs`
- Test: `tests/Streaming.UnitTests/DubStabilizerTest.cs`

**Interfaces:**
- Consumes: `Transcript` (`Text`, `IsStable`, `Languages`).
- Produces: `enum DubDecision { Undecided, Dub, NoDub }`; `DubStabilizer` with `string SentText`, `string? Next(Transcript translated)` (the not-yet-sent stable suffix, or null), `static DubDecision Decide(Transcript source, Transcript translated, Language targetLanguage)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Numerics;
using ActualChat.Transcription;

namespace ActualChat.Streaming.UnitTests;

public class DubStabilizerTest
{
    [Fact]
    public void NextShouldSkipUnstableTranscripts()
    {
        var stabilizer = new DubStabilizer();

        stabilizer.Next(Unstable("Hello wor")).Should().BeNull();
        stabilizer.SentText.Should().BeEmpty();
    }

    [Fact]
    public void NextShouldReturnOnlyTheNewStableSuffix()
    {
        var stabilizer = new DubStabilizer();

        stabilizer.Next(Stable("Hello world.")).Should().Be("Hello world.");
        stabilizer.Next(Stable("Hello world.")).Should().BeNull("nothing new is stable");
        stabilizer.Next(Unstable("Hello world. How are")).Should().BeNull();
        stabilizer.Next(Stable("Hello world. How are you?")).Should().Be(" How are you?");
        stabilizer.SentText.Should().Be("Hello world. How are you?");
    }

    [Fact]
    public void NextShouldResendFromTheDivergencePoint()
    {
        var stabilizer = new DubStabilizer();
        stabilizer.Next(Stable("Hello world."));

        var chunk = stabilizer.Next(Stable("Hello there, world."));

        chunk.Should().Be("there, world.",
            "TTS can't retract, so the divergent tail is spoken again rather than lost");
        stabilizer.SentText.Should().Be("Hello there, world.");
    }

    [Fact]
    public void NextShouldIgnoreWhitespaceOnlyGrowth()
    {
        var stabilizer = new DubStabilizer();
        stabilizer.Next(Stable("Hello."));

        stabilizer.Next(Stable("Hello. ")).Should().BeNull();
    }

    [Fact]
    public void DecideShouldSayNoDubWhenTheSourceIsAlreadyInTheTargetLanguage()
        => DubStabilizer
            .Decide(Stable("Hello there, how are you?", Languages.English), Unstable(""), Language.Parse("en-GB"))
            .Should().Be(DubDecision.NoDub);

    [Fact]
    public void DecideShouldSayDubWhenTheSourceLanguageDiffers()
        => DubStabilizer
            .Decide(Stable("Привет, как у тебя дела?", Languages.Russian), Unstable(""), Languages.English)
            .Should().Be(DubDecision.Dub);

    [Fact]
    public void DecideShouldWaitForEnoughTextBeforeTrustingTheLanguage()
        => DubStabilizer
            .Decide(Stable("Hi", Languages.English), Unstable(""), Languages.English)
            .Should().Be(DubDecision.Undecided);

    [Fact]
    public void DecideShouldSayNoDubWhenTheTranslationRepeatsTheSource()
        => DubStabilizer
            .Decide(Unstable("Hello there, how are you doing"), Stable("Hello there, how are you"), Languages.English)
            .Should().Be(DubDecision.NoDub, "the translator hands the text back verbatim when no translation is needed");

    [Fact]
    public void DecideShouldSayDubWhenTheTranslationDiffers()
        => DubStabilizer
            .Decide(Unstable("Привет, как у тебя сегодня дела"), Stable("Hello, how are you today"), Languages.English)
            .Should().Be(DubDecision.Dub);

    [Fact]
    public void DecideShouldStayUndecidedWhileTheTranslationIsUnstableOrShort()
    {
        DubStabilizer.Decide(Unstable("Привет, как дела"), Unstable("Hello, how are you"), Languages.English)
            .Should().Be(DubDecision.Undecided);
        DubStabilizer.Decide(Unstable("Привет"), Stable("Hi"), Languages.English)
            .Should().Be(DubDecision.Undecided);
    }

    private static Transcript Stable(string text, params Language[] languages)
        => Unstable(text, languages) with { IsStable = true };

    private static Transcript Unstable(string text, params Language[] languages)
        => new(text, LinearMap.Zero.Append(new Vector2(text.Length, text.Length)), languages);
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Streaming.UnitTests/Streaming.UnitTests.csproj --filter "FullyQualifiedName~DubStabilizerTest"`
Expected: build error.

- [ ] **Step 3: Write the stabilizer**

```csharp
using System.Text.RegularExpressions;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

public enum DubDecision
{
    Undecided,
    Dub,
    NoDub,
}

/// <summary>
/// Turns a translated transcript stream into text a TTS engine may speak - only the stable prefix,
/// only what wasn't sent yet - and decides whether the source needs dubbing at all.
/// </summary>
public sealed partial class DubStabilizer
{
    private const int MinDecisionLength = 10;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegexFactory();
    private static readonly Regex WhitespaceRegex = WhitespaceRegexFactory();

    public string SentText { get; private set; } = "";

    public string? Next(Transcript translated)
    {
        if (!translated.IsStable)
            return null;

        var text = translated.Text;
        var prefixLength = text.StartsWith(SentText) ? SentText.Length : CommonPrefixLength(text, SentText);
        var chunk = text[prefixLength..];
        if (chunk.IsNullOrWhiteSpace())
            return null;

        SentText = text;
        return chunk;
    }

    public static DubDecision Decide(Transcript source, Transcript translated, Language targetLanguage)
    {
        if (source.Languages.Length > 0 && source.Text.Length >= MinDecisionLength)
            return source.Languages.Any(x => x.IsoCode == targetLanguage.IsoCode)
                ? DubDecision.NoDub
                : DubDecision.Dub;
        if (!translated.IsStable)
            return DubDecision.Undecided;

        var translatedText = Normalize(translated.Text);
        if (translatedText.Length < MinDecisionLength)
            return DubDecision.Undecided;

        // The translator hands the source text back verbatim when no translation is needed
        return Normalize(source.Text).StartsWith(translatedText)
            ? DubDecision.NoDub
            : DubDecision.Dub;
    }

    // Private methods

    private static string Normalize(string text)
        => WhitespaceRegex.Replace(text, " ").Trim().ToLower();

    private static int CommonPrefixLength(string x, string y)
    {
        var length = Math.Min(x.Length, y.Length);
        var i = 0;
        while (i < length && x[i] == y[i])
            i++;
        return i;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Streaming.UnitTests/Streaming.UnitTests.csproj --filter "FullyQualifiedName~DubStabilizerTest"`
Expected: 10 passed.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Streaming.Service/Audio/DubStabilizer.cs tests/Streaming.UnitTests/DubStabilizerTest.cs
git commit -m "feat(dubbing): DubStabilizer - stable-suffix feed and dub/no-dub decision"
```

---

### Task 8: Lazy dub in `AudioStreamingBackend`

**Files:**
- Create: `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.Dubbing.cs`
- Modify: `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.cs` (`GetAudio`, `GetTranscript`, `_transcriptStreams.OnStreamExpire`, `RememberChatId`, `ForgetChatIdIfUnused`)
- Modify: `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs:129` (remember the author)
- Test: `tests/Streaming.IntegrationTests/DubbingTest.cs`

**Interfaces:**
- Consumes: `ISpeechSynthesizer` (optional service), `DubStabilizer`/`DubDecision` (Task 7), `Constants.Audio.DubWaitTimeout` (Task 1), `TranslationsBackend_TranslateStream`, `ActualOpusStreamHeader`.
- Produces: `IAudioStreamingBackend.GetAudio(S~lang, skipTo)` now starts the dub of `S` into `lang` on first call and returns its stream, or `null` when the speaker already speaks `lang` (NoDub), no synthesizer is registered, or nothing was decided within `DubWaitTimeout`. Internals: `Task<bool> EnsureDub(StreamId)`, `Task<AsyncMemoizer<TranscriptDiff>?> GetOrStartTranslation(StreamId)`, `_authorIdByStream`, `RememberAuthorId(StreamId, AuthorId)`.

- [ ] **Step 1: Write the failing integration test**

```csharp
using System.Numerics;
using ActualChat.Audio;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(DubbingCollection))]
public class DubbingTest(DubbingCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingCollection.AppHostFixture>(fixture, @out)
{
    [Fact(Timeout = 60_000)]
    public async Task GetAudioShouldPublishADubForATranslatedTranscript()
    {
        // arrange
        var services = AppHost.Services;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.Russian);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = cts.Token;
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var translated = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var pushTranslatedTask = BackgroundTask.Run(
            () => backend.PushTranscript(dubId, new RpcStream<TranscriptDiff>(translated.Reader.ReadAllAsync(ct)), ct),
            ct);
        source.Writer.TryWrite(Stable("Hello there, how are you doing today?") - Transcript.Empty);
        translated.Writer.TryWrite(Stable("Привет, как у тебя сегодня дела?") - Transcript.Empty);

        // act
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);

        // assert
        stream.Should().NotBeNull("the fake synthesizer dubs any translated text");
        var frames = new List<AudioFrame>();
        await foreach (var frame in stream!.WithCancellation(ct)) {
            frames.Add(frame);
            if (frames.Count >= 3)
                break;
        }
        frames[0].Offset.Should().Be(TimeSpan.FromMilliseconds(-1), "the first frame is the stream header");
        frames.Skip(1).Select(f => f.Offset).Should().Equal(TimeSpan.Zero, Constants.Audio.OpusFrameDuration);

        source.Writer.Complete();
        translated.Writer.Complete();
        await pushSourceTask.SilentAwait(false);
        await pushTranslatedTask.SilentAwait(false);
    }

    [Fact(Timeout = 60_000)]
    public async Task GetAudioShouldReturnNullWhenTheSourceIsAlreadyInTheTargetLanguage()
    {
        // arrange
        var services = AppHost.Services;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.English);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = cts.Token;
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var translated = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var pushTranslatedTask = BackgroundTask.Run(
            () => backend.PushTranscript(dubId, new RpcStream<TranscriptDiff>(translated.Reader.ReadAllAsync(ct)), ct),
            ct);
        var text = Stable("Hello there, how are you doing today?", Languages.English);
        source.Writer.TryWrite(text - Transcript.Empty);
        translated.Writer.TryWrite(text - Transcript.Empty);

        // act
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);

        // assert
        stream.Should().BeNull("an English speaker isn't dubbed into English");

        source.Writer.Complete();
        translated.Writer.Complete();
        await pushSourceTask.SilentAwait(false);
        await pushTranslatedTask.SilentAwait(false);
    }

    private static Transcript Stable(string text, params Language[] languages)
        => new(text, LinearMap.Zero.Append(new Vector2(text.Length, text.Length)), languages) { IsStable = true };
}

[CollectionDefinition(nameof(DubbingCollection))]
public sealed class DubbingCollection : ICollectionFixture<DubbingCollection.AppHostFixture>
{
    public sealed class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture(
            "dubbing",
            messageSink,
            TestAppHostOptions.Default with {
                ConfigureServices = (_, services) => {
                    services.AddSingleton<ISpeechSynthesizer>(c => new FakeSpeechSynthesizer(c));
                },
            });
}
```

Check `tests/Streaming.IntegrationTests/Collections/` for how `StreamingCollection` declares its fixture and mirror any extra options it sets (e.g. instance name conventions); `RetranscribeTranslationCollection` in `tests/Chat.IntegrationTests/RetranscribeTranslationFlowTest.cs:129` is the closest template for `ConfigureServices`.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Streaming.IntegrationTests/Streaming.IntegrationTests.csproj --filter "FullyQualifiedName~DubbingTest"`
Expected: `GetAudioShouldPublishADub…` fails (`stream` is null — today `GetAudio` waits 2 s for an unpublished stream and returns null); the NoDub test passes trivially. Both compile.

- [ ] **Step 3: Remember the author, forget it with the chat id**

`AudioStreamingBackend.cs`: add the field next to `_chatIdByStream`:

```csharp
    private readonly ConcurrentDictionary<StreamId, AuthorId> _authorIdByStream = new();
```

Add next to `RememberChatId`:

```csharp
    internal void RememberAuthorId(StreamId streamId, AuthorId authorId)
        => _authorIdByStream[BaseStreamId(streamId)] = authorId;
```

In `ForgetChatIdIfUnused`, after `_chatIdByStream.TryRemove(baseStreamId, out _);` add `_authorIdByStream.TryRemove(baseStreamId, out _);` (inside the same `if`). In `ProcessAudio.cs` after `RememberChatId(openSegment.StreamId, chatId);` add `RememberAuthorId(openSegment.StreamId, author.Id);`.

- [ ] **Step 4: Factor the translation start out of `GetTranscript`**

Replace the body of `GetTranscript` in `AudioStreamingBackend.cs` with:

```csharp
    public virtual async Task<RpcStream<TranscriptDiff>?> GetTranscript(
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("GetTranscript: #{StreamId}", streamId);
        var memoizer = await GetOrStartTranslation(streamId, cancellationToken).ConfigureAwait(false);
        return memoizer == null
            ? null
            : StandardRpcStream.NewTranscriptDelivery(memoizer.Replay(_transcriptStreams.ReplayTailSize, cancellationToken));
    }
```

and add to the `// Private methods` section:

```csharp
    // A source transcript is returned as published or not at all; a translated one (language suffix)
    // is started on first request and waited for.
    private async Task<AsyncMemoizer<TranscriptDiff>?> GetOrStartTranslation(
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        var memoizer = await _transcriptStreams.GetMemoizer(streamId, false, cancellationToken).ConfigureAwait(false);
        if (memoizer != null)
            return memoizer;

        var language = streamId.Language;
        if (language == null)
            return null;

        var originalStreamId = BaseStreamId(streamId);
        if (_translatingStreams.TryAdd(streamId, originalStreamId)) {
            DebugLog?.LogDebug("GetOrStartTranslation: #{StreamId} - Translate stream", streamId);
            var cmd = new TranslationsBackend_TranslateStream(originalStreamId, language);
            // Use ApplicationStopping as the caller might be canceled, but we still want to wait
            // for the translated stream to be created.
            await Commander.Call(cmd, HostLifetime.StopToken()).ConfigureAwait(false);
        }
        return await _transcriptStreams.GetMemoizer(streamId, true, cancellationToken).ConfigureAwait(false);
    }
```

- [ ] **Step 5: Hook `GetAudio` and stream expiry**

In `GetAudio`, before the `if (skipTo == Constants.Audio.SkipToLive)` line:

```csharp
        if (streamId.Language != null
            && !_audioStreams.Has(streamId)
            && !await EnsureDub(streamId, cancellationToken).ConfigureAwait(false))
            return null;

```

In the constructor, `_transcriptStreams.OnStreamExpire`:

```csharp
            OnStreamExpire = id => {
                _translatingStreams.Remove(id, out _);
                ForgetDubs(id);
                ForgetChatIdIfUnused(id);
            },
```

- [ ] **Step 6: Write the dubbing partial**

```csharp
using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

public partial class AudioStreamingBackend
{
    private readonly ConcurrentDictionary<StreamId, DubEntry> _dubs = new();
    private readonly ConcurrentDictionary<string, Task> _dubChains = new();

    private ISpeechSynthesizer? SpeechSynthesizer => field ??= Services.GetService<ISpeechSynthesizer>();

    // Starts the dub of dubStreamId's base stream into dubStreamId.Language unless it's running or
    // decided already. True means the dub stream is published; false means serve the original.
    private async Task<bool> EnsureDub(StreamId dubStreamId, CancellationToken cancellationToken)
    {
        if (SpeechSynthesizer == null)
            return false;

        var entry = _dubs.GetOrAdd(dubStreamId, static (id, self) => self.StartDub(id), this);
        try {
            return await entry.WhenDecided
                .WaitAsync(Constants.Audio.DubWaitTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException) {
            Log.LogWarning("EnsureDub: #{StreamId} - no decision in {Timeout}s, serving the original",
                dubStreamId, Constants.Audio.DubWaitTimeout.TotalSeconds);
            return false;
        }
    }

    private DubEntry StartDub(StreamId dubStreamId)
    {
        var decidedSource = TaskCompletionSourceExt.New<bool>();
#pragma warning disable CA2016 // Pass cancellationToken
        var stopTokenSource = HostLifetime.CreateStopTokenSource();
#pragma warning restore CA2016
        var worker = FuncWorker.New(
            static (arg, ct) => arg.self.RunDub(arg.dubStreamId, arg.decidedSource, ct),
            (self: this, dubStreamId, decidedSource),
            stopTokenSource);
        worker.Start();
        return new DubEntry(worker, decidedSource.Task);
    }

    private async Task RunDub(
        StreamId dubStreamId,
        TaskCompletionSource<bool> decidedSource,
        CancellationToken cancellationToken)
    {
        var sourceStreamId = BaseStreamId(dubStreamId);
        var language = dubStreamId.Language!;
        var text = Channel.CreateUnbounded<string>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
        });
        Task? synthesizeTask = null;
        Exception? error = null;
        try {
            var sourceMemoizer = await _transcriptStreams
                .GetMemoizer(sourceStreamId, true, cancellationToken)
                .ConfigureAwait(false);
            var translatedMemoizer = await GetOrStartTranslation(dubStreamId, cancellationToken).ConfigureAwait(false);
            if (sourceMemoizer == null || translatedMemoizer == null) {
                Log.LogWarning("RunDub: #{StreamId} - no transcript to dub", dubStreamId);
                return;
            }

            var stabilizer = new DubStabilizer();
            var decision = DubDecision.Undecided;
            var translated = Transcript.Empty;
            await foreach (var diff in translatedMemoizer.Replay(cancellationToken).ConfigureAwait(false)) {
                translated += diff;
                if (decision == DubDecision.Undecided) {
                    decision = DubStabilizer.Decide(Fold(sourceMemoizer), translated, language);
                    if (decision == DubDecision.NoDub) {
                        Log.LogInformation("RunDub: #{StreamId} - already in {Language}", dubStreamId, language);
                        return;
                    }
                    if (decision == DubDecision.Dub)
                        synthesizeTask = StartSynthesis(dubStreamId, text.Reader, decidedSource, cancellationToken);
                }
                if (decision != DubDecision.Dub)
                    continue;

                if (stabilizer.Next(translated) is { } chunk)
                    await text.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
            if (decision == DubDecision.Undecided)
                Log.LogInformation("RunDub: #{StreamId} - too short to decide, not dubbed", dubStreamId);
        }
        catch (Exception e) {
            error = e;
            if (!e.IsCancellationOf(cancellationToken))
                Log.LogError(e, "RunDub: #{StreamId} failed", dubStreamId);
        }
        finally {
            decidedSource.TrySetResult(false);
            text.Writer.TryComplete(error);
            if (synthesizeTask != null)
                await synthesizeTask.SilentAwait(false);
        }
    }

    private Task StartSynthesis(
        StreamId dubStreamId,
        ChannelReader<string> text,
        TaskCompletionSource<bool> decidedSource,
        CancellationToken cancellationToken)
    {
        var language = dubStreamId.Language!;
        var frames = Channel.CreateUnbounded<AudioFrame>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
        });
        var header = new AudioFrame {
            Data = new ActualOpusStreamHeader(Clocks.ServerClock.Now, AudioSource.DefaultFormat).Serialize(),
            Offset = TimeSpan.FromMilliseconds(-1),
        };
        var memoizer = frames.Reader.ReadAllAsync(cancellationToken).Prepend(header).Memoize(cancellationToken);
        if (!_audioStreams.Publish(dubStreamId, memoizer)) {
            _ = memoizer.DisposeAsync();
            throw StandardError.Internal($"Dub stream #{dubStreamId} is already published.");
        }

        decidedSource.TrySetResult(true);
        var previousDubTask = ChainDub(dubStreamId, out var whenDoneSource, out var chainKey);
        return BackgroundTask.Run(async () => {
            try {
                // One voice must not overlap itself: the author's previous dub in this language
                // may still be draining after its source ended.
                await previousDubTask.SilentAwait(false);
                await SpeechSynthesizer!
                    .Synthesize(dubStreamId.Value, text, new SpeechSynthesisOptions(language), frames.Writer, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) {
                frames.Writer.TryComplete(e);
                throw;
            }
            finally {
                frames.Writer.TryComplete();
                whenDoneSource.TrySetResult();
                if (chainKey != null)
                    _dubChains.TryRemove(new KeyValuePair<string, Task>(chainKey, whenDoneSource.Task));
            }
        }, Log, $"Dub #{dubStreamId} failed", cancellationToken);
    }

    private Task ChainDub(StreamId dubStreamId, out TaskCompletionSource whenDoneSource, out string? chainKey)
    {
        whenDoneSource = TaskCompletionSourceExt.New();
        chainKey = null;
        if (!_authorIdByStream.TryGetValue(BaseStreamId(dubStreamId), out var authorId))
            return Task.CompletedTask;

        chainKey = $"{authorId}~{dubStreamId.Language}";
        var previousDubTask = _dubChains.GetValueOrDefault(chainKey) ?? Task.CompletedTask;
        _dubChains[chainKey] = whenDoneSource.Task;
        return previousDubTask;
    }

    private void ForgetDubs(StreamId streamId)
    {
        var baseStreamId = BaseStreamId(streamId);
        foreach (var dubStreamId in _dubs.Keys)
            if (BaseStreamId(dubStreamId) == baseStreamId && _dubs.TryRemove(dubStreamId, out var entry))
                _ = entry.Worker.DisposeSilentlyAsync();
    }

    private static Transcript Fold(AsyncMemoizer<TranscriptDiff> memoizer)
    {
        var (transcript, _) = memoizer is FoldingAsyncMemoizer<TranscriptDiff, Transcript> folding
            ? folding.Fold()
            : memoizer.FoldBuffered(Transcript.Empty, TranscriptFolder);
        return transcript;
    }

    // Nested types

    private sealed record DubEntry(FuncWorker Worker, Task<bool> WhenDecided);
}
```

Notes for the implementer: `GetTranscriptSnapshot` in the same class shows the exact `Fold()`/`FoldBuffered` tuple shape — match it. The `SpeechSynthesizer!` is safe because `EnsureDub` gates on it. `Synthesize` completes `frames.Writer` itself on success (the pump does), the extra `TryComplete` in `finally` is a no-op then and the safety net otherwise.

- [ ] **Step 7: Run the integration tests**

Run: `dotnet test tests/Streaming.IntegrationTests/Streaming.IntegrationTests.csproj --filter "FullyQualifiedName~DubbingTest"`
Expected: 2 passed.
Run: `dotnet test tests/Streaming.IntegrationTests/Streaming.IntegrationTests.csproj --filter "FullyQualifiedName~TranscriptSnapshotTest|FullyQualifiedName~LiveAudioStreamsTest"`
Expected: all passed (the `GetTranscript` refactor is behavior-neutral).

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/Streaming.Service/Backend tests/Streaming.IntegrationTests/DubbingTest.cs
git commit -m "feat(dubbing): GetAudio(S~lang) starts a lazily-dubbed audio stream"
```

---

### Task 9: Muxer substitution

**Files:**
- Modify: `src/dotnet/Streaming.Service/Services/ListeningStreamMuxer.cs`
- Test: `tests/Streaming.UnitTests/ListeningStreamMuxerTest.cs`

**Interfaces:**
- Consumes: `LiveAudioStreamInfo.Languages/MaySpeak/DubLanguage` (Task 6), `GetAudio(S~lang)` via `LiveAudioStreams.GetStream` (Task 8).
- Produces: `ListeningStreamMuxer(services, session, chatId, catchUpFrom = default, dubLanguage = null)`; `internal static bool MustDub(LiveAudioStreamInfo, Language?)`; `StreamEntry.IsDubbed`. A dubbed entry asks for `S~dubLanguage`, falls back to `S` when that returns null, emits its start item with `DubLanguage` set and `BeginsAt`/`SourceBeginsAt` = the moment the dub started flowing, and is exempt from the per-author merge.

- [ ] **Step 1: Write the failing tests**

Add to `ListeningStreamMuxerTest`:

```csharp
    [Fact]
    public void MustDubShouldRequireASpeakerWhoDoesNotSpeakTheListenersLanguage()
    {
        var russianSpeaker = StreamInfo(Author1, "s", Now()) with { Languages = new ApiArray<Language>([Languages.Russian]) };
        var bilingual = StreamInfo(Author1, "s", Now()) with {
            Languages = new ApiArray<Language>([Languages.Russian, Language.Parse("en-US")]),
        };
        var unknown = StreamInfo(Author1, "s", Now());

        ListeningStreamMuxer.MustDub(russianSpeaker, Languages.English).Should().BeTrue();
        ListeningStreamMuxer.MustDub(russianSpeaker, Languages.Russian).Should().BeFalse();
        ListeningStreamMuxer.MustDub(bilingual, Language.Parse("en-GB")).Should().BeFalse("English variants match");
        ListeningStreamMuxer.MustDub(russianSpeaker, null).Should().BeFalse("no dub language requested");
        ListeningStreamMuxer.MustDub(unknown, Languages.English).Should().BeFalse(
            "a stream from an older server carries no languages, and guessing would hold audio for nothing");
    }

    [Fact]
    public void TryRegisterShouldNotMergeDubbedStreamsOfTheSameAuthor()
    {
        var h = new MuxerHarness();
        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();
        var t1 = Now();
        var t2 = t1 + TimeSpan.FromSeconds(5);

        h.Register(Author1, "stream-1", t1, cts1, isDubbed: true).Should().BeTrue();
        h.Register(Author1, "stream-2", t2, cts2, isDubbed: true).Should().BeTrue();

        cts1.IsCancellationRequested.Should().BeFalse(
            "a dub outlives its source by the translation lag plus the spoken length, so the next "
            + "utterance must not cut it; the backend serializes dubs per author instead");
        h.HasById("stream-1").Should().BeTrue();
        h.HasById("stream-2").Should().BeTrue();
        h.GetActiveStreamId(Author1).Should().BeNull("dubbed entries stay out of the per-author map");
    }
```

Update the harness `Register` signature to `Register(AuthorId authorId, string streamId, Moment beginsAt, CancellationTokenSource cts, bool isDubbed = false)` and after `var entry = Activator.CreateInstance(...)`:

```csharp
            StreamEntryType.GetProperty("IsDubbed")!.SetValue(entry, isDubbed);
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Streaming.UnitTests/Streaming.UnitTests.csproj --filter "FullyQualifiedName~ListeningStreamMuxerTest"`
Expected: build error (`MustDub`, `IsDubbed`).

- [ ] **Step 3: Change the muxer**

Constructor and properties:

```csharp
    private Language? DubLanguage { get; }
    private MomentClockSet Clocks => field ??= Services.Clocks();

    public ListeningStreamMuxer(
        IServiceProvider services, Session session, ChatId chatId, Moment catchUpFrom = default,
        Language? dubLanguage = null)
    {
        ...
        DubLanguage = dubLanguage;
```

Next to `GetSkipTo`:

```csharp
    // internal for tests
    internal static bool MustDub(LiveAudioStreamInfo streamInfo, Language? dubLanguage)
        => dubLanguage != null && streamInfo.Languages.Count > 0 && !streamInfo.MaySpeak(dubLanguage);
```

In `OnRun`, the `StreamEntry` initializer gains `IsDubbed = MustDub(streamInfo, DubLanguage),`.

In `ProcessStream`, replace the `GetStream` call and the start-item construction:

```csharp
            var skipTo = GetSkipTo(streamEntry.IsPreexisting, streamInfo, CatchUpFrom);
            var (rpcStream, startInfo) = await GetStream(streamEntry, skipTo, streamStopToken).ConfigureAwait(false);
            if (rpcStream == null) {
                ...unchanged
            }

            await foreach (var frame in rpcStream.ConfigureAwait(false)) {
                if (frameCount == 0) {
                    var startItem = new MuxedAudioStreamStart() {
                        StreamIndex = streamIndex,
                        StreamInfo = startInfo,
                    };
```

Add under `// Per-author stream management` (before `TryRegister`):

```csharp
    private async Task<(RpcStream<AudioFrame>? Stream, LiveAudioStreamInfo StartInfo)> GetStream(
        StreamEntry entry,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var streamInfo = entry.StreamInfo;
        if (entry.IsDubbed) {
            var dubStreamId = StreamId.New(StreamId.Parse(streamInfo.StreamId), DubLanguage!).Value;
            var dub = await LiveAudioStreams
                .GetStream(Session, dubStreamId, skipTo, cancellationToken)
                .ConfigureAwait(false);
            if (dub != null) {
                // The dub's own timeline: lag metrics and the idle cue must not see the translation delay
                var now = Clocks.ServerClock.Now;
                return (dub, streamInfo with { DubLanguage = DubLanguage, BeginsAt = now, SourceBeginsAt = now });
            }

            Log.LogInformation("GetStream: no {Language} dub for #{StreamId}, serving the original",
                DubLanguage, streamInfo.StreamId);
        }
        var original = await LiveAudioStreams
            .GetStream(Session, streamInfo.StreamId, skipTo, cancellationToken)
            .ConfigureAwait(false);
        return (original, streamInfo);
    }
```

`TryRegister`:

```csharp
    private bool TryRegister(StreamEntry entry)
    {
        _streamById[entry.StreamId] = entry;
        // A dub outlives its source by the translation lag plus the spoken length, so the author's
        // next utterance must not evict it; the backend serializes an author's dubs instead.
        if (entry.IsDubbed)
            return true;

        return ReferenceEquals(entry, _streamByAuthor.AddOrUpdate(
```

`StreamEntry` gains `public bool IsDubbed { get; init; }` next to `IsPreexisting`. Add `using ActualChat.Audio;` if `RpcStream<AudioFrame>` needs it.

- [ ] **Step 4: Run the muxer tests**

Run: `dotnet test tests/Streaming.UnitTests/Streaming.UnitTests.csproj --filter "FullyQualifiedName~ListeningStreamMuxerTest"`
Expected: all passed (the harness's reflection setup initialises every `ConcurrentDictionary` field, so the new `Clocks` property needs nothing).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Streaming.Service/Services/ListeningStreamMuxer.cs tests/Streaming.UnitTests/ListeningStreamMuxerTest.cs
git commit -m "feat(dubbing): muxer serves the dub instead of the original for a dubbing listener"
```

---

### Task 10: `GetListeningStream` with `dubLanguage`

**Files:**
- Modify: `src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs`
- Modify: `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs:164-176`

**Interfaces:**
- Produces: `Task<RpcStream<MuxedAudioStreamItem>> GetListeningStream(Session session, ChatId chatId, Moment catchUpFrom, Language? dubLanguage, CancellationToken ct)` — a distinct wire method (`:5`); the 4-arg one stays for published clients and delegates with `null`.

- [ ] **Step 1: Extend the contract**

After the existing 4-arg `GetListeningStream` in `ILiveAudioStreams`:

```csharp
    // dubLanguage: speakers who don't speak it are served dubbed into it (see ListeningStreamMuxer)
    Task<RpcStream<MuxedAudioStreamItem>> GetListeningStream(
        Session session,
        ChatId chatId,
        Moment catchUpFrom,
        Language? dubLanguage,
        CancellationToken cancellationToken);
```

- [ ] **Step 2: Implement**

In `LiveAudioStreams`, turn the existing method into a delegate and add the real one:

```csharp
    public Task<RpcStream<MuxedAudioStreamItem>> GetListeningStream(
        Session session,
        ChatId chatId,
        Moment catchUpFrom,
        CancellationToken cancellationToken)
        => GetListeningStream(session, chatId, catchUpFrom, null, cancellationToken);

    public async Task<RpcStream<MuxedAudioStreamItem>> GetListeningStream(
        Session session,
        ChatId chatId,
        Moment catchUpFrom,
        Language? dubLanguage,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        chat.Rules.Require(ChatPermissions.ReadAudio);

        Log.LogInformation("GetListeningStream: chat '{ChatId}', catchUpFrom={CatchUpFrom}, dub={DubLanguage}",
            chatId, catchUpFrom, dubLanguage);
        var muxer = new ListeningStreamMuxer(Services, session, chatId, catchUpFrom, dubLanguage);
        var stream = ToLiveAsyncEnumerable(muxer, muxer.Output, cancellationToken);
        return StandardRpcStream.NewAudioDelivery(stream, allowReconnect: false);
    }
```

- [ ] **Step 3: Build and run the API-level streaming tests**

Run: `dotnet build src/dotnet/Streaming.Service/Streaming.Service.csproj && dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Expected: both succeed (the client still calls the 4-arg overload).
Run: `dotnet test tests/Streaming.IntegrationTests/Streaming.IntegrationTests.csproj --filter "FullyQualifiedName~LiveAudioStreamsTest|FullyQualifiedName~LivePlaybackTest"`
Expected: all passed.

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs
git commit -m "feat(dubbing): GetListeningStream takes the listener's dub language"
```

---

### Task 11: Settings and `TranslationUI.GetDubLanguage`

**Files:**
- Modify: `src/dotnet/Api/Users/UserLanguageSettings.cs`
- Modify: `src/dotnet/Api/Chat/StoredSettings/ChatUserSettings.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/TranslationUI/TranslationUI.cs`

**Interfaces:**
- Produces: `UserLanguageSettings.IsTranslatedVoiceEnabled` (bool, key 6, MemoryPackOrder 6), `ChatUserSettings.IsTranslatedVoiceEnabled` (bool?, key 8, MemoryPackOrder 9; null = inherit); `TranslationUI.GetDubLanguage(ChatId, ct) → Language?` (null unless translation is enabled for the chat and translated voice is effectively on); `TranslationUI.SetTranslatedVoice(ChatId, bool?, ct)`.

- [ ] **Step 1: Add the settings members**

`UserLanguageSettings`, after `DetectedUILanguage`:

```csharp
    // Listener side of voice dubbing: other-language speakers in live sessions are heard dubbed
    [DataMember, MemoryPackOrder(6), Key(6)]
    public bool IsTranslatedVoiceEnabled { get; init; }
```

`ChatUserSettings`, after `MustTranslateOwnMessages`:

```csharp
    // null = follow UserLanguageSettings.IsTranslatedVoiceEnabled
    [DataMember, MemoryPackOrder(9), Key(8)] public bool? IsTranslatedVoiceEnabled { get; init; }
```

- [ ] **Step 2: Add the compute method and setter**

In `TranslationUI`, after `GetTranslationLanguage`:

```csharp
    [ComputeMethod]
    public virtual async Task<Language?> GetDubLanguage(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (await IsEnabled(chatId, cancellationToken).ConfigureAwait(false) != true)
            return null;

        var chatSetting = await UserSettingsUI.ChatUserSettings(GetTranslationSettingsTargetChatId(chatId))
            .Get(x => x.IsTranslatedVoiceEnabled, cancellationToken)
            .ConfigureAwait(false);
        var isEnabled = chatSetting
            ?? (await LanguageUI.Settings.Use(LanguageUI.WhenReady, cancellationToken).ConfigureAwait(false))
                .IsTranslatedVoiceEnabled;
        if (!isEnabled)
            return null;

        return await GetTranslationLanguage(chatId, cancellationToken).ConfigureAwait(false);
    }
```

After `SetTargetLanguage`:

```csharp
    public Task SetTranslatedVoice(ChatId chatId, bool? value, CancellationToken cancellationToken = default)
        => UserSettingsUI.ChatUserSettings(GetTranslationSettingsTargetChatId(chatId))
            .Update(x => x with { IsTranslatedVoiceEnabled = value }, cancellationToken);
```

- [ ] **Step 3: Build and run the serialization tests that cover stored settings**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Expected: Build succeeded.
Run: `dotnet test tests/Users.UnitTests/Users.UnitTests.csproj` and `dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj --filter "FullyQualifiedName~Settings"`
Expected: all passed (a `Legacy*` mirror test failing here means a key collision — re-check the numbers against the file).

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/Api/Users/UserLanguageSettings.cs src/dotnet/Api/Chat/StoredSettings/ChatUserSettings.cs src/dotnet/UI.Blazor.App/Services/TranslationUI/TranslationUI.cs
git commit -m "feat(dubbing): translated-voice settings and TranslationUI.GetDubLanguage"
```

---

### Task 12: Client listens with the dub language

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/Audio/ListeningStreamProcessor.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/Playback/ChatListeningPlayer.cs`

**Interfaces:**
- Consumes: 5-arg `GetListeningStream` (Task 10), `TranslationUI.GetDubLanguage` (Task 11).
- Produces: `ListeningStreamProcessor.DubLanguageProvider` (`Func<CancellationToken, Task<Language?>>?`, init), read on every (re)connect; `ChatListeningPlayer` breaks the stream when the dub language changes, exactly like the sleep watcher.

- [ ] **Step 1: Processor**

Add the property after `CatchUpFrom`:

```csharp
    public Func<CancellationToken, Task<Language?>>? DubLanguageProvider { get; init; }
```

In the `Provider` lambda, replace the `GetListeningStream` call:

```csharp
                var dubLanguage = DubLanguageProvider == null
                    ? null
                    : await DubLanguageProvider.Invoke(ct).ConfigureAwait(false);
                Log.LogInformation(
                    "-> LiveStreams.GetListeningStream({ChatId}), catchUpFrom={CatchUpFrom}, dub={DubLanguage}",
                    ChatId, catchUpFrom, dubLanguage);
                var stream = await liveStreams
                    .GetListeningStream(Session, ChatId, catchUpFrom, dubLanguage, ct)
                    .ConfigureAwait(false);
```

- [ ] **Step 2: Player**

In `ChatListeningPlayer.Play`:

```csharp
        var streamProcessor = new ListeningStreamProcessor(
            Hub.Services, Session, ChatId,
            ChatAudioUI.GetListeningCatchUp(ChatId),
            cancellationToken.CreateLinkedTokenSource()) {
            DubLanguageProvider = ct => Hub.TranslationUI.GetDubLanguage(ChatId, ct),
        };
        await using var _ = streamProcessor.ConfigureAwait(false);

        streamProcessor.StreamStarted +=
            (info, _, frames) => OnStreamStarted(playback, state, info, frames, cancellationToken);
        StartSleepWatcher(streamProcessor, cancellationToken);
        StartDubLanguageWatcher(streamProcessor, cancellationToken);
```

Add after `ResubscribeOnSleep`:

```csharp
    private void StartDubLanguageWatcher(
        ListeningStreamProcessor streamProcessor,
        CancellationToken cancellationToken)
        => _ = BackgroundTask.Run(
            () => ResubscribeOnDubLanguageChange(streamProcessor, cancellationToken),
            Log,
            $"Dub language watcher failed for #{ChatId}",
            cancellationToken);

    private async Task ResubscribeOnDubLanguageChange(
        ListeningStreamProcessor streamProcessor,
        CancellationToken cancellationToken)
    {
        var computed = await Computed
            .Capture(() => Hub.TranslationUI.GetDubLanguage(ChatId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (true) {
            await computed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            var previous = computed.Value;
            computed = await computed.Update(cancellationToken).ConfigureAwait(false);
            if (computed.Value == previous)
                continue;

            Log.LogInformation("Re-subscribing to #{ChatId}: dub language {Old} -> {New}",
                ChatId, previous, computed.Value);
            streamProcessor.Break();
        }
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/Audio/ListeningStreamProcessor.cs src/dotnet/UI.Blazor.App/Services/Playback/ChatListeningPlayer.cs
git commit -m "feat(dubbing): listening player requests the dub language and re-subscribes on change"
```

---

### Task 13: Settings toggle and localization

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/Settings/TranscriptionSettings.razor`
- Modify: `src/dotnet/Localization/Resources/Strings.en.json` and every hand-written `Strings.<lang>.json` (bg, bs, cs, de, es, fr, hi, id, it, ja, ko, pl, pt, ru, tr, uk, vi, zh)
- Modify: `src/dotnet/Localization/Resources/LocalizedStringsLocalizerExt.cs`
- Regenerate: `Strings.cnr.json`, `Strings.hr.json`, `Strings.sr.json`, `Strings.max.json`

**Interfaces:**
- Consumes: `LanguageUI.UpdateSettings`, `UserLanguageSettings.IsTranslatedVoiceEnabled` (Task 11).
- Produces: keys `Transcription_TranslatedVoiceTopic`, `Transcription_TranslatedVoice`, `Transcription_TranslatedVoiceCaption`, `LiveConversation_TranslatedVoice` (the last one is consumed by Task 14).

- [ ] **Step 1: Add the keys**

`Strings.en.json`, right after `Transcription_KeepListeningTopic`'s group (keep the file's alphabetical-within-group order):

```json
  "Transcription_TranslatedVoiceTopic": "Translated Voice",
  "Transcription_TranslatedVoice": "Hear translations spoken",
  "Transcription_TranslatedVoiceCaption": "In live conversations, speakers of other languages are replaced by a spoken translation in your language",
```

and in the live-conversation group (next to `Call_Join` if there is no `LiveConversation_` group yet — then add the group with a one-line context comment in the style of the neighbours):

```json
  "LiveConversation_TranslatedVoice": "Translated voice",
```

Translate all four into each hand-written catalog (Russian, for reference: `"Переведённый голос"`, `"Слушать перевод голосом"`, `"В живых разговорах речь на других языках заменяется озвученным переводом на ваш язык"`, `"Переведённый голос"`). Then:

```bash
python3 scripts/l10n/derive-bcms.py
python3 scripts/l10n/derive-max.py
```

`LocalizedStringsLocalizerExt.cs` — add four members next to `Transcription_FaceDownStopCaption` / the `Call_` members:

```csharp
        public string Transcription_TranslatedVoiceTopic => l["Transcription_TranslatedVoiceTopic"].Value;
        public string Transcription_TranslatedVoice => l["Transcription_TranslatedVoice"].Value;
        public string Transcription_TranslatedVoiceCaption => l["Transcription_TranslatedVoiceCaption"].Value;
        public string LiveConversation_TranslatedVoice => l["LiveConversation_TranslatedVoice"].Value;
```

- [ ] **Step 2: Run the localization test**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~AppLocalizationTest"`
Expected: all passed (it fails naming any catalog missing a key).

- [ ] **Step 3: Add the toggle**

`TranscriptionSettings.razor`, after the `ListeningLingerSettings` tile:

```razor
<TileTopic Topic="@L.Transcription_TranslatedVoiceTopic"/>
<Tile>
    <TileItem Click="@OnToggleTranslatedVoice">
        <Icon><i class="icon-headphones-fill text-2xl"></i></Icon>
        <Content>@L.Transcription_TranslatedVoice</Content>
        <Caption>@L.Transcription_TranslatedVoiceCaption</Caption>
        <Right>
            <Toggle IsChecked="@m.Languages.IsTranslatedVoiceEnabled" IsCheckedChanged="@(_ => OnToggleTranslatedVoice())"/>
        </Right>
    </TileItem>
</Tile>
```

In `@code`, next to `OnToggleFaceDownStop`:

```csharp
    private Task OnToggleTranslatedVoice()
        => LanguageUI.UpdateSettings(x => x with { IsTranslatedVoiceEnabled = !x.IsTranslatedVoiceEnabled });
```

- [ ] **Step 4: Build and check it renders**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Expected: Build succeeded. Then, with the watch server running (`tmp/watch-dotnet.log` shows `Now listening on:`), open Settings → Transcription and flip the toggle twice; it must persist across a reload.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/Settings/TranscriptionSettings.razor src/dotnet/Localization/Resources
git commit -m "feat(dubbing): translated-voice toggle in transcription settings"
```

---

### Task 14: Per-chat chip in the live header

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/LiveConversationHeaderState.cs`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/LiveConversationHeaderView.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/conversation.css`

Read `docs/ui/components.md` first (CSS class naming, where child styles belong).

**Interfaces:**
- Consumes: `TranslationUI.GetDubLanguage/SetTranslatedVoice/IsEnabled` (Task 11), `L.LiveConversation_TranslatedVoice` (Task 13).
- Produces: header state `IsTranslatedVoiceAvailable` (user-level setting on ∧ translation enabled for this chat ∧ live) and `IsTranslatedVoiceOn`; a `HeaderButton.c-lc-dub` that toggles the per-chat override.

- [ ] **Step 1: State**

```csharp
public sealed record LiveConversationHeaderState(
    string Title,
    string ParticipantsText,
    bool IsJoined = false,
    bool HasAttended = false,
    bool IsDissolving = false,
    bool CanExpand = false,
    bool IsAnyoneTalking = false,
    bool IsTranslatedVoiceAvailable = false,
    bool IsTranslatedVoiceOn = false);
```

- [ ] **Step 2: View**

Markup, after the `Join` button and before `@if (s.CanExpand)`:

```razor
    @if (s.IsTranslatedVoiceAvailable) {
        <HeaderButton
            Class="@("c-lc-dub" + (s.IsTranslatedVoiceOn ? " on" : ""))"
            Tooltip="@L.LiveConversation_TranslatedVoice"
            Click="@ToggleTranslatedVoice">
            <i class="icon-headphones-fill"></i>
        </HeaderButton>
    }
```

`@code`: add `private TranslationUI TranslationUI => Hub.TranslationUI;` and `private LanguageUI LanguageUI => Hub.LanguageUI;`. In `ComputeState`, after `isAnyoneTalking`:

```csharp
        var isTranslationOn = isLive
            && await TranslationUI.IsEnabled(chatId, cancellationToken).ConfigureAwait(false) == true;
        var isTranslatedVoiceAvailable = isTranslationOn
            && (await LanguageUI.Settings.Use(LanguageUI.WhenReady, cancellationToken).ConfigureAwait(false))
                .IsTranslatedVoiceEnabled;
        var isTranslatedVoiceOn = isTranslatedVoiceAvailable
            && await TranslationUI.GetDubLanguage(chatId, cancellationToken).ConfigureAwait(false) != null;
        return new LiveConversationHeaderState(
            translated.Title.Text, participantsText, isJoined,
            block?.HasAttended ?? false, isDissolving, canExpand,
            isAnyoneTalking, isTranslatedVoiceAvailable, isTranslatedVoiceOn);
```

and:

```csharp
    private Task ToggleTranslatedVoice()
        // Off is a per-chat override; on is "inherit", since the chip only shows when the user-level setting is on
        => TranslationUI.SetTranslatedVoice(Header.Conversation!.Id.ChatId, State.Value.IsTranslatedVoiceOn ? false : null);
```

- [ ] **Step 3: CSS**

In `conversation.css`, next to the `.live-conversation-header` rules (~line 415):

```css
.virtual-list .c-virtual-container .live-conversation-header .c-lc-dub {
    @apply flex-none text-03;
}
.virtual-list .c-virtual-container .live-conversation-header .c-lc-dub.on {
    @apply text-primary;
}
.virtual-list .c-virtual-container .live-conversation-header .c-lc-dub i {
    @apply text-xl;
}
```

- [ ] **Step 4: Build and verify with two users**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj` — Build succeeded.
Then the manual pass (use `/debug-ui` for fast multi-user sign-in): user A (Russian primary language) records in a chat; user B (English primary, translation enabled for the chat, translated voice on) listens. Expect: B's live header shows the headphones chip highlighted; after ~2–3 s B hears an English stock voice instead of A; toggling the chip off restores A's voice on the next utterance; the server log shows `GetListeningStream … dub=en`, `EnsureDub`/`RunDub` lines, and `GetStream: no … dub` only if the decision timed out.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation
git commit -m "feat(dubbing): per-chat translated-voice chip in the live header"
```

---

### Task 15: Pipeline doc

**Files:**
- Create: `docs/live-audio/12-dubbing.md`
- Modify: `docs/live-audio/README.md` (index entry)

- [ ] **Step 1: Write the doc with `/docs-master`**

Invoke the `docs-master` skill, then write `12-dubbing.md` in the style of `05-server-publish-and-transcribe.md`: the flow diagram from the spec's "Components and data flow" section (updated for the deviations listed at the top of this plan), the lazy start in `GetAudio`, `DubStabilizer`'s rules, `OpusFramePump` pacing and silence fill, the muxer's `MustDub`/fallback/merge exemption, the settings and `GetDubLanguage`, constants (`DubWaitTimeout`, `TtsMaxStreamDuration`, `TtsKeepAlivePeriod`), and a "Not yet" list (marker, cue, mixed-language switch, clones, replay). Add the entry to `README.md`.

- [ ] **Step 2: Commit**

```bash
git add docs/live-audio/12-dubbing.md docs/live-audio/README.md
git commit -m "docs(live-audio): voice dubbing pipeline"
```

---

## Done criteria for this plan

- All unit tests in `Transcription.UnitTests`, `Streaming.UnitTests`, `Chat.UI.Blazor.UnitTests` (localization) pass; `DubbingTest`, `TranscriptSnapshotTest`, `LiveAudioStreamsTest`, `LivePlaybackTest` pass; the two Soniox spike tests passed at least once with the real key (paste their output lines into the PR).
- The two-device manual pass in Task 14 done on a local server; note the observed lag.
- Then ask the developer whether to run `/prepare-merge` before `/create-pr`.
