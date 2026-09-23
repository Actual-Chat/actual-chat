# Speaking Bots Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an integration speak in a chat — bringing its own audio and transcript, or being read aloud by server-side synthesis — finalizing into the same playable `ChatEntryAudio` a person's recording produces.

**Architecture:** A producer pushes audio frames and text deltas; `ExternalTranscriptBuilder` turns them into `TranscriptDiff`s and a `LinearMap`, and the existing `ProcessAudio` machinery persists and fans out the result. Server transcription is bypassed only in this explicit mode. Separately, a text-only bot entry can be spoken on demand by running the existing dub path in a new same-language **speak mode**.

**Tech Stack:** C# 15 / .NET 11, ActualLab.Fusion + Rpc, Ogg Opus (`OggOpusReader`), xUnit + FluentAssertions, MCP (ModelContextProtocol).

**Spec:** [`docs/superpowers/specs/2026-09-23-speaking-bots-design.md`](../specs/2026-09-23-speaking-bots-design.md)

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing any C#. No `Async` suffix; no new `///` on members; Allman braces for types/methods, K&R everywhere else; 120-char lines; `.ConfigureAwait(false)` in service code.
- Every serializable type needs `[DataContract, MessagePackObject]` plus `[DataMember(Order = N), Key(N)]` on each member. Never renumber a shipped `Key`.
- All test waits go through `ActualChat.Testing.TestWait` — never `ComputedTest.When` or `TestExt.When`. See `docs/testing/waiting.md`.
- Tests use `Should<ExpectedBehavior>` naming, FluentAssertions, and lowercase `// arrange` / `// act` / `// assert`.
- Native `ILiveAudioStreams.PushStream` wire behaviour must remain byte-compatible and keep using server transcription.
- Maintenance mode blocks all new external streams in an affected chat, re-checked per chunk.
- A stream always attributes content to the authenticated caller. No impersonation on this path.
- Audio offsets are **media positions**, never wall-clock arrival times.
- Build with `dotnet build <project>` — `ActualChat.CI.slnf` is stale and fails locally.

## Review Focus

1. **An Ogg page split across two MCP appends** — a bot chunking at arbitrary byte boundaries must not lose or corrupt the frame straddling them. Covered in Task 5, Step 1.
2. **Audio with no text at all** — a bot that speaks but sends no transcript should still finalize a playable entry, not an empty or failed one. Covered in Task 4, Step 7.
3. **Speak mode requested on an entry that already has audio** — a human voice message must never be synthesized over. Covered in Task 6, Step 7.
4. **A replacement chunk shorter than the text already mapped** — `IsAppend: false` shrinking the transcript must truncate the map, not leave points past the end. Covered in Task 2, Step 9.
5. **Header-only or empty Ogg** — a stream whose audio duration is zero must not produce a `LinearMap` with an infinite or NaN slope. Covered in Task 2, Step 11.

---

## Reuse

### Existing abstractions to reuse

Research done against the tree at `a1c1e1d03b`; each of these is called, not reimplemented.

- **Synthesis** — `ISpeechSynthesizer.Synthesize(streamId, ChannelReader<string>, SpeechSynthesisOptions, ChannelWriter<byte[]>, ct)`, `SonioxSpeechSynthesizer`, `FakeSpeechSynthesizer` (tests).
- **Dubbing** — `AudioStreamingBackend.Dubbing.cs`: `EnsureDub`, `RunDub`, `PublishMix`, `WaitForOriginal`, `DecideOnSource`, `DubStabilizer`, `DubSynthesizerDownDelay`; `VoiceOverMix` (its `original` is nullable, which is what makes dub-only work); `ReplayDubs`.
- **Voice identity** — `SpeakerVoices.Get(chatId, authorId, ct)`, with its clone → picked `DubVoice` → accent default → synthesizer default chain.
- **Ogg Opus** — `OggOpusReader` (`Append`/`TryRead` for incremental input, `ReadFrames` for streams, `PreSkip`, `FrameCount`, `Head`), `OggOpusStreamConverter`, `RequireMono`, `ActualOpusStreamHeader`, `AudioSource`.
- **Audio ingestion** — `AudioStreamingBackend.ProcessAudio.cs` for validation, registration, `OpenAudioSegment`, Opus persistence, media finalization and stream fixup; `AudioRecord`; `AudioFrame.Offset`/`Duration`.
- **Transcripts** — `Transcript(Text, TimeMap, Languages)`, `TranscriptDiff.New(transcript, baseTranscript)`, `StringDiff`, `LinearMapDiff`, `LinearMap` (`IsValid`, `IsDegenerate`, `XRange`, `YRange`), `IAudioStreamingBackend.PushTranscript`, `StreamStore`.
- **Lease mechanics** — `ChatEntryStreams` as the structural model, `ExpiringEntry<TKey,TValue>`, `StreamId` + `MeshRefResolvers` node routing, the offset-checked append contract from `Uploads_Append`.
- **Guards** — `MaintenanceStreamExt.RequireAvailable`, `MaintenanceExt.RequireAvailable`, `ChatEntryFlags.IsViaApi`.
- **Tests** — `TestWait`, `McpTestBase` (`IssueApiKey`, `CreateClient`, `CallTool`, `CallToolExpectingError`), `FakeTranscriber`, `WebClientTester`.

Nothing existing builds a time map from producer-supplied or derived offsets, and nothing runs a
same-language dub. Those two are genuinely new, and are Tasks 2 and 6.

### Reusability of new components

| New component | Placement | Why |
|---|---|---|
| `ExternalTranscriptChunk`, `ExternalTranscriptBuilder` | **`ActualChat.Api/Transcription/`** (shared) | Recommended. Offset→map construction is wanted by chat import and by any future transcription provider, not just this path. It depends only on `Transcript`/`LinearMap`, so `ActualChat.Core` is not an option — `Api` is the shared home that fits. The alternative, `Streaming.Service`, would bury a pure, reusable algorithm inside one service. |
| `ExternalTranscriptStreamer` | `Streaming.Service/Services/` (local) | It coordinates this one ingestion path and has no second caller. Per "wait for the second caller", keep it local. |
| `ChatVoiceStreams` + `IChatVoiceStreamsBackend` | `Chat.Service` / `Chat.Contracts` (local) | Mirrors `ChatEntryStreams`, which is already local for the same reason. If a third lease appears, extract the shared lease mechanics then — the shape will be knowable at that point and is not now. |
| Speak mode | Inside `AudioStreamingBackend.Dubbing.cs` (local) | A mode of dubbing, not a new responsibility. Task 6 adds one predicate and two branches; if `RunDub` becomes hard to read afterwards, extracting the speak path is a follow-up, not part of this plan. |
| MCP voice tools | `Mcp/Tools/McpMessageTools.cs` (local) | Adapter only. The plan's own rule is that transport logic stays out of MCP tools. |

---

## File Structure

**Create:**
- `src/dotnet/Api/Transcription/ExternalTranscriptChunk.cs` — the wire type for one text delta.
- `src/dotnet/Api/Transcription/ExternalTranscriptBuilder.cs` — chunks + ingested audio duration → `TranscriptDiff`s and a final `Transcript`.
- `src/dotnet/Streaming.Service/Services/ExternalTranscriptStreamer.cs` — coordinates the audio and transcript streams for one utterance.
- `src/dotnet/Chat.Service/ChatVoiceStreams.cs` — the node-pinned lease backing the MCP voice tools.
- `src/dotnet/Chat.Contracts/IChatVoiceStreamsBackend.cs` — its backend contract.
- `src/dotnet/Api/Chat/ChatVoiceStream.cs` — the lease's result model.
- `src/dotnet/Mcp/Models/McpVoiceStream.cs` — the MCP-facing shape.

**Modify:**
- `src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs` — add `PushAudioWithTranscript`.
- `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs` — implement it; enforce the speak gate.
- `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs` — split so the transcript producer is selectable.
- `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.Dubbing.cs` — speak mode.
- `src/dotnet/Streaming.Service/Services/ReplayDubs.cs` — speak mode on replay.
- `src/dotnet/Mcp/Tools/McpMessageTools.cs` — the three voice tools.
- `src/dotnet/Api/Constants.cs` — voice-stream limits.
- `docs/integrations/streaming-writes.md` — the audio modes.

---

### Task 1: The external transcript chunk contract

**Files:**
- Create: `src/dotnet/Api/Transcription/ExternalTranscriptChunk.cs`
- Test: `tests/Streaming.UnitTests/ExternalTranscriptContractTest.cs`

**Interfaces:**
- Produces: `ExternalTranscriptChunk(string Text, bool IsAppend, double? AudioOffset, bool IsStable)` — consumed by Tasks 2, 4, 5.

- [ ] **Step 1: Write the failing serialization test**

```csharp
using ActualChat.Transcription;

namespace ActualChat.Streaming.UnitTests;

public class ExternalTranscriptContractTest
{
    [Fact]
    public void ChunkShouldRoundTripThroughEverySerializer()
    {
        // arrange
        var chunk = new ExternalTranscriptChunk("Hello", true, 1.25, true);

        // act, assert
        chunk.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void ChunkShouldRoundTripWithoutAnOffset()
    {
        // arrange - a producer with no alignment data leaves AudioOffset null
        var chunk = new ExternalTranscriptChunk("Hello", true, null, false);

        // act
        var restored = chunk.PassThroughAllSerializers();

        // assert
        restored.AudioOffset.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run: `dotnet test tests/Streaming.UnitTests --filter ExternalTranscriptContractTest`
Expected: FAIL — `ExternalTranscriptChunk` does not exist.

- [ ] **Step 3: Create the type**

```csharp
namespace ActualChat.Transcription;

/// <summary>
/// One incremental transcript update from an external producer. <see cref="AudioOffset"/> is a
/// position in the producer's own audio, never a timestamp; null lets the server derive it.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ExternalTranscriptChunk(
    [property: DataMember(Order = 0), Key(0)] string Text,
    [property: DataMember(Order = 1), Key(1)] bool IsAppend,
    [property: DataMember(Order = 2), Key(2)] double? AudioOffset,
    [property: DataMember(Order = 3), Key(3)] bool IsStable);
```

- [ ] **Step 4: Run the test and verify it passes**

Run: `dotnet test tests/Streaming.UnitTests --filter ExternalTranscriptContractTest`
Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Api/Transcription/ExternalTranscriptChunk.cs tests/Streaming.UnitTests/ExternalTranscriptContractTest.cs
git commit -m "feat(transcription): define the external transcript chunk"
```

---

### Task 2: The transcript and time-map builder

This is the algorithmic core and the highest-risk piece. It is pure — no infrastructure — so every edge case is a unit test.

**Files:**
- Create: `src/dotnet/Api/Transcription/ExternalTranscriptBuilder.cs`
- Test: `tests/Streaming.UnitTests/ExternalTranscriptBuilderTest.cs`

**Interfaces:**
- Consumes: `ExternalTranscriptChunk` (Task 1); `Transcript`, `TranscriptDiff`, `LinearMap` (existing).
- Produces:
  - `ExternalTranscriptBuilder.Append(ExternalTranscriptChunk chunk, TimeSpan ingestedAudioDuration) -> TranscriptDiff?` — null when the chunk changes nothing.
  - `ExternalTranscriptBuilder.Finalize(TimeSpan finalAudioDuration) -> Transcript`
  - `ExternalTranscriptBuilder.Transcript { get; }` — the current transcript.

Consumed by Tasks 4 and 5.

- [ ] **Step 1: Write the failing append/offset tests**

```csharp
using ActualChat.Transcription;

namespace ActualChat.Streaming.UnitTests;

public class ExternalTranscriptBuilderTest
{
    [Fact]
    public void ShouldAppendTextAndUseTheSuppliedOffset()
    {
        // arrange
        var builder = new ExternalTranscriptBuilder();

        // act
        var diff = builder.Append(new ExternalTranscriptChunk("Hello", true, 1.0, true), TimeSpan.Zero);

        // assert
        diff.Should().NotBeNull();
        builder.Transcript.Text.Should().Be("Hello");
        builder.Transcript.TimeMap.YRange.End.Should().BeApproximately(1.0f, 0.001f);
        builder.Transcript.TimeMap.XRange.End.Should().Be(5);
    }

    [Fact]
    public void ShouldDeriveTheOffsetFromIngestedAudioWhenAbsent()
    {
        // arrange
        var builder = new ExternalTranscriptBuilder();

        // act - no offset supplied, but 2s of audio has already arrived
        builder.Append(new ExternalTranscriptChunk("Hello", true, null, true), TimeSpan.FromSeconds(2));

        // assert
        builder.Transcript.TimeMap.YRange.End.Should().BeApproximately(2.0f, 0.001f);
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test tests/Streaming.UnitTests --filter ExternalTranscriptBuilderTest`
Expected: FAIL — `ExternalTranscriptBuilder` does not exist.

- [ ] **Step 3: Implement the minimal builder**

```csharp
using System.Numerics;

namespace ActualChat.Transcription;

/// <summary>
/// Turns an external producer's text deltas into <see cref="TranscriptDiff"/>s and a time map.
/// Offsets a producer omits are derived from the audio ingested so far.
/// </summary>
public sealed class ExternalTranscriptBuilder
{
    private readonly List<Vector2> _points = [new(0, 0)];

    public Transcript Transcript { get; private set; } = Transcript.Empty;

    public TranscriptDiff? Append(ExternalTranscriptChunk chunk, TimeSpan ingestedAudioDuration)
    {
        var text = chunk.IsAppend ? Transcript.Text + chunk.Text : chunk.Text;
        var offset = (float)(chunk.AudioOffset ?? ingestedAudioDuration.TotalSeconds);
        if (!float.IsFinite(offset) || offset < 0)
            throw StandardError.Constraint("Audio offset must be finite and non-negative.");
        if (text.Length > Constants.Chat.MaxEntryTextLength)
            throw StandardError.Constraint(
                $"A message can hold up to {Constants.Chat.MaxEntryTextLength} characters.");

        var baseTranscript = Transcript;
        AddPoint(text.Length, offset);
        Transcript = new Transcript(text, new LinearMap(CollectionsMarshal.AsSpan(_points)), [])
            { IsStable = chunk.IsStable };
        var diff = TranscriptDiff.New(Transcript, baseTranscript);
        return diff.IsNone ? null : diff;
    }

    public Transcript Finalize(TimeSpan finalAudioDuration)
        => Transcript;

    // Private methods

    private void AddPoint(int textLength, float offset)
    {
        // The map must stay non-decreasing on both axes, or seeking inverts
        var last = _points[^1];
        var x = Math.Max(last.X, textLength);
        var y = Math.Max(last.Y, offset);
        if (x <= last.X && y <= last.Y)
            return;

        if (_points.Count > 1 && Math.Abs(last.X - x) < float.Epsilon)
            _points[^1] = new Vector2(x, y);
        else
            _points.Add(new Vector2(x, y));
    }
}
```

Add `using System.Runtime.InteropServices;` for `CollectionsMarshal`.

- [ ] **Step 4: Run the tests and verify they pass**

Run: `dotnet test tests/Streaming.UnitTests --filter ExternalTranscriptBuilderTest`
Expected: PASS, 2 tests.

- [ ] **Step 5: Write the failing replacement and monotonicity tests**

```csharp
    [Fact]
    public void ShouldReplaceTheWholeTextWhenNotAppending()
    {
        // arrange - a recognizer correcting itself
        var builder = new ExternalTranscriptBuilder();
        builder.Append(new ExternalTranscriptChunk("Helo wrld", true, 1.0, false), TimeSpan.Zero);

        // act
        builder.Append(new ExternalTranscriptChunk("Hello world", false, 1.2, true), TimeSpan.Zero);

        // assert
        builder.Transcript.Text.Should().Be("Hello world");
    }

    [Fact]
    public void ShouldClampADecreasingOffset()
    {
        // arrange
        var builder = new ExternalTranscriptBuilder();
        builder.Append(new ExternalTranscriptChunk("One ", true, 2.0, true), TimeSpan.Zero);

        // act - a producer that goes backwards must not invert the map
        builder.Append(new ExternalTranscriptChunk("two", true, 1.0, true), TimeSpan.Zero);

        // assert
        builder.Transcript.TimeMap.IsValid().Should().BeTrue();
        builder.Transcript.TimeMap.YRange.End.Should().BeApproximately(2.0f, 0.001f);
    }

    [Fact]
    public void ShouldRejectANonFiniteOffset()
    {
        // arrange
        var builder = new ExternalTranscriptBuilder();

        // act
        var append = () => builder.Append(
            new ExternalTranscriptChunk("x", true, double.NaN, true), TimeSpan.Zero);

        // assert
        append.Should().Throw<Exception>();
    }
```

- [ ] **Step 6: Run and verify the replacement test fails**

Run: `dotnet test tests/Streaming.UnitTests --filter ExternalTranscriptBuilderTest`
Expected: `ShouldReplaceTheWholeTextWhenNotAppending` FAILs — the map still holds points past the shortened text. The other two pass.

- [ ] **Step 7: Truncate the map on replacement**

Add to `Append`, before `AddPoint`:

```csharp
        if (!chunk.IsAppend)
            TruncatePointsTo(text.Length);
```

And the helper:

```csharp
    private void TruncatePointsTo(int textLength)
    {
        // A replacement can shorten the text; points past its end would map characters that
        // no longer exist, which inverts seeking rather than merely being imprecise
        for (var i = _points.Count - 1; i > 0; i--) {
            if (_points[i].X <= textLength)
                break;

            _points.RemoveAt(i);
        }
    }
```

- [ ] **Step 8: Run the tests and verify they pass**

Run: `dotnet test tests/Streaming.UnitTests --filter ExternalTranscriptBuilderTest`
Expected: PASS, 5 tests.

- [ ] **Step 9: Write the failing shrink-past-map test (Review Focus #4)**

```csharp
    [Fact]
    public void ShouldTruncateTheMapWhenAReplacementShrinksTheText()
    {
        // arrange - three mapped chunks, then a correction far shorter than all of them
        var builder = new ExternalTranscriptBuilder();
        builder.Append(new ExternalTranscriptChunk("One ", true, 1.0, true), TimeSpan.Zero);
        builder.Append(new ExternalTranscriptChunk("two ", true, 2.0, true), TimeSpan.Zero);
        builder.Append(new ExternalTranscriptChunk("three", true, 3.0, true), TimeSpan.Zero);

        // act
        builder.Append(new ExternalTranscriptChunk("Hi", false, 3.5, true), TimeSpan.Zero);

        // assert
        builder.Transcript.Text.Should().Be("Hi");
        builder.Transcript.TimeMap.XRange.End.Should().Be(2,
            "no map point may sit past the end of the text");
        builder.Transcript.TimeMap.IsValid().Should().BeTrue();
    }
```

- [ ] **Step 10: Run it and verify it passes**

Run: `dotnet test tests/Streaming.UnitTests --filter ShouldTruncateTheMapWhenAReplacementShrinksTheText`
Expected: PASS — Step 7 already implemented this. If it fails, `TruncatePointsTo` is removing the wrong end.

- [ ] **Step 11: Write the failing finalize tests (degenerate map + Review Focus #5)**

```csharp
    [Fact]
    public void ShouldSpreadADegenerateMapAcrossTheFinalDuration()
    {
        // arrange - all text first, all audio last: every derived offset is zero
        var builder = new ExternalTranscriptBuilder();
        builder.Append(new ExternalTranscriptChunk("One ", true, null, true), TimeSpan.Zero);
        builder.Append(new ExternalTranscriptChunk("two ", true, null, true), TimeSpan.Zero);
        builder.Append(new ExternalTranscriptChunk("three", true, null, true), TimeSpan.Zero);

        // act
        var transcript = builder.Finalize(TimeSpan.FromSeconds(6));

        // assert
        transcript.TimeMap.IsDegenerate.Should().BeFalse();
        transcript.TimeMap.YRange.End.Should().BeApproximately(6.0f, 0.001f);
        transcript.TimeMap.IsValid().Should().BeTrue();
    }

    [Fact]
    public void ShouldFinalizeWithoutAudioDuration()
    {
        // arrange - header-only Ogg: no frames, so no duration
        var builder = new ExternalTranscriptBuilder();
        builder.Append(new ExternalTranscriptChunk("Hello", true, null, true), TimeSpan.Zero);

        // act
        var transcript = builder.Finalize(TimeSpan.Zero);

        // assert
        transcript.Text.Should().Be("Hello");
        transcript.TimeMap.Data.Should().OnlyContain(v => float.IsFinite(v));
    }

    [Fact]
    public void ShouldRejectAnOffsetPastTheFinalDuration()
    {
        // arrange - the one case where the producer is wrong about its own media
        var builder = new ExternalTranscriptBuilder();
        builder.Append(new ExternalTranscriptChunk("Hello", true, 30.0, true), TimeSpan.Zero);

        // act
        var finalize = () => builder.Finalize(TimeSpan.FromSeconds(5));

        // assert
        finalize.Should().Throw<Exception>();
    }
```

- [ ] **Step 12: Run and verify the finalize tests fail**

Run: `dotnet test tests/Streaming.UnitTests --filter ExternalTranscriptBuilderTest`
Expected: the three new tests FAIL — `Finalize` currently returns the transcript untouched.

- [ ] **Step 13: Implement Finalize**

```csharp
    public Transcript Finalize(TimeSpan finalAudioDuration)
    {
        var duration = (float)finalAudioDuration.TotalSeconds;
        if (Transcript.Text.Length == 0)
            return Transcript;
        if (_points[^1].Y > duration + MaxOffsetOvershoot && duration > 0)
            throw StandardError.Constraint(
                $"A transcript offset ({_points[^1].Y:F2}s) lies past the audio ({duration:F2}s).");

        // Every offset derived to the same value - the producer sent its text and its audio in
        // separate bursts. Spreading the text across the audio is approximate; refusing is worse.
        if (duration > 0 && _points[^1].Y <= float.Epsilon) {
            _points.Clear();
            _points.Add(new Vector2(0, 0));
            _points.Add(new Vector2(Transcript.Text.Length, duration));
        }
        else if (duration > _points[^1].Y)
            AddPoint(Transcript.Text.Length, duration);

        Transcript = Transcript with {
            TimeMap = new LinearMap(CollectionsMarshal.AsSpan(_points)),
            IsStable = true,
        };
        return Transcript;
    }
```

Add the constant to the class:

```csharp
    // Producers round their own offsets; a fraction of a second past the audio is not a lie
    private const float MaxOffsetOvershoot = 0.5f;
```

- [ ] **Step 14: Run the full builder suite**

Run: `dotnet test tests/Streaming.UnitTests --filter ExternalTranscriptBuilderTest`
Expected: PASS, 9 tests.

- [ ] **Step 15: Write the shape-from-life tests**

```csharp
    [Fact]
    public void ShouldHandleLlmSizedFragments()
    {
        // arrange - an LLM emits a token at a time, each with the audio spoken so far
        var builder = new ExternalTranscriptBuilder();
        var fragments = new[] { "The ", "quick ", "brown ", "fox " };

        // act
        for (var i = 0; i < fragments.Length; i++)
            builder.Append(new ExternalTranscriptChunk(fragments[i], true, null, true),
                TimeSpan.FromSeconds(0.5 * (i + 1)));
        var transcript = builder.Finalize(TimeSpan.FromSeconds(2));

        // assert
        transcript.Text.Should().Be("The quick brown fox ");
        transcript.TimeMap.IsValid().Should().BeTrue();
        transcript.TimeMap.Length.Should().BeGreaterThan(2, "each fragment should be seekable");
    }

    [Fact]
    public void ShouldHandleRecognizerCorrections()
    {
        // arrange - a recognizer replaces its whole hypothesis repeatedly
        var builder = new ExternalTranscriptBuilder();

        // act
        builder.Append(new ExternalTranscriptChunk("I scream", false, 1.0, false), TimeSpan.Zero);
        builder.Append(new ExternalTranscriptChunk("ice cream", false, 1.1, false), TimeSpan.Zero);
        builder.Append(new ExternalTranscriptChunk("ice cream cone", false, 2.0, true), TimeSpan.Zero);
        var transcript = builder.Finalize(TimeSpan.FromSeconds(2));

        // assert
        transcript.Text.Should().Be("ice cream cone");
        transcript.TimeMap.IsValid().Should().BeTrue();
    }
```

- [ ] **Step 16: Run the tests and verify they pass**

Run: `dotnet test tests/Streaming.UnitTests --filter ExternalTranscriptBuilderTest`
Expected: PASS, 11 tests. If `ShouldHandleRecognizerCorrections` fails on map validity, `TruncatePointsTo` is leaving a point whose X exceeds the new text length — check its `<=` boundary.

- [ ] **Step 17: Commit**

```bash
git add src/dotnet/Api/Transcription/ExternalTranscriptBuilder.cs tests/Streaming.UnitTests/ExternalTranscriptBuilderTest.cs
git commit -m "feat(transcription): build transcripts and time maps from external chunks"
```

---

### Task 3: Make the transcript producer selectable in ProcessAudio

`ProcessAudio` currently hard-wires ASR. Task 4 needs the same persistence, registration and fan-out with a different transcript source. This task is a pure refactor — behaviour must not change, and the existing tests are the proof.

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs`
- Test: `tests/Streaming.IntegrationTests/LiveAudioStreamsTest.cs` (existing — must stay green)

**Interfaces:**
- Produces: `AudioStreamingBackend.ProcessAudioInternal(AudioRecord record, int preSkip, RpcStream<AudioFrame> frameStream, TranscriptSource transcriptSource, CancellationToken)` where

```csharp
public delegate IAsyncEnumerable<TranscriptDiff> TranscriptSource(
    OpenAudioSegment segment,
    CancellationToken cancellationToken);
```

Consumed by Task 4.

- [ ] **Step 1: Record the current behaviour**

Run: `dotnet test tests/Streaming.IntegrationTests --filter LiveAudioStreamsTest`
Write the passing count here before touching anything; it is the number Step 5 must reproduce.

- [ ] **Step 2: Read the method and find the transcription seam**

Open `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs`. Locate where the transcriber is selected and invoked (search for `TranscriberSelector` and `PushTranscript`). Everything above it — validation, registration, `OpenAudioSegment` creation — and everything below it — Opus persistence, media finalization, unregistration — is shared. Only the transcriber call is the variable part.

- [ ] **Step 3: Introduce the delegate without changing any caller**

Add the delegate next to the class, extract the current body into `ProcessAudioInternal` taking a `TranscriptSource`, and make `ProcessAudio` call it with the ASR source:

```csharp
    public Task ProcessAudio(
        AudioRecord record,
        int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken)
        => ProcessAudioInternal(record, preSkip, frameStream, TranscribeWithAsr, cancellationToken);
```

where `TranscribeWithAsr` is the existing transcriber invocation, moved verbatim into a private method matching `TranscriptSource`.

- [ ] **Step 4: Build**

Run: `dotnet build src/dotnet/Streaming.Service`
Expected: 0 errors.

- [ ] **Step 5: Prove the native path did not move**

Run: `dotnet test tests/Streaming.IntegrationTests --filter LiveAudioStreamsTest`
Expected: the same passing count as Step 1, zero failures. A single new failure here means the refactor changed behaviour — fix it before continuing rather than adjusting the test.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs
git commit -m "refactor(streaming): make the transcript producer selectable in ProcessAudio"
```

---

### Task 4: Push audio with an external transcript over RPC

**Files:**
- Modify: `src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs`
- Create: `src/dotnet/Streaming.Service/Services/ExternalTranscriptStreamer.cs`
- Modify: `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs`
- Modify: `src/dotnet/Streaming.Service/Module/StreamingServiceModule.cs`
- Test: `tests/Streaming.IntegrationTests/ExternalAudioTranscriptStreamTest.cs`

**Interfaces:**
- Consumes: `ExternalTranscriptChunk` (Task 1), `ExternalTranscriptBuilder` (Task 2), `ProcessAudioInternal` + `TranscriptSource` (Task 3).
- Produces:

```csharp
Task<ChatEntry> PushAudioWithTranscript(
    Session session,
    string chatId,
    string? repliedChatEntryId,
    double clientStartAt,
    int preSkip,
    RpcStream<AudioFrame> frameStream,
    RpcStream<ExternalTranscriptChunk> transcriptStream,
    CancellationToken cancellationToken);
```

Consumed by Task 5.

- [ ] **Step 1: Write the failing end-to-end test**

```csharp
using ActualChat.Audio;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class ExternalAudioTranscriptStreamTest(
    StreamingCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<StreamingCollection.AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private IChatsBackend ChatsBackend => field ??= AppHost.Services.GetRequiredService<IChatsBackend>();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldFinalizeAPlayableEntryFromExternalAudioAndText()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var frames = await TestAudio.ReadFrames("test-audio-1.wav");

        // act
        var entry = await Tester.LiveAudioStreams.PushAudioWithTranscript(
            Tester.Session, chatId.Value, null, Tester.Clocks().SystemClock.Now.EpochOffset.TotalSeconds,
            0,
            RpcStream.New(frames.ToAsyncEnumerable()),
            RpcStream.New(new[] {
                new ExternalTranscriptChunk("Hello ", true, 0.5, true),
                new ExternalTranscriptChunk("world", true, 1.0, true),
            }.ToAsyncEnumerable()),
            CancellationToken.None);

        // assert
        entry.Content.Should().Be("Hello world");
        entry.IsContentStreaming.Should().BeFalse();
        entry.Audio.Should().NotBeNull();
        entry.Audio!.TimeMap.IsDegenerate.Should().BeFalse();
    }
}
```

`TestAudio.ReadFrames` is the existing helper used by `LiveAudioStreamsTest`; if it is named differently there, use that name.

- [ ] **Step 2: Run and verify it fails**

Run: `dotnet test tests/Streaming.IntegrationTests --filter ExternalAudioTranscriptStreamTest`
Expected: FAIL — `PushAudioWithTranscript` does not exist.

- [ ] **Step 3: Add the contract method**

In `ILiveAudioStreams.cs`, directly below `PushStream`:

```csharp
    // The producer supplies the transcript, so server ASR is bypassed. Offsets in the chunks are
    // positions in the producer's own audio; a chunk without one is pinned to the audio ingested
    // so far. Unlike PushStream, this returns the finalized entry.
    [RpcMethod(ConnectTimeout = double.PositiveInfinity)]
    Task<ChatEntry> PushAudioWithTranscript(
        Session session,
        string chatId,
        string? repliedChatEntryId,
        double clientStartAt,
        int preSkip,
        RpcStream<AudioFrame> frameStream,
        RpcStream<ExternalTranscriptChunk> transcriptStream,
        CancellationToken cancellationToken);
```

- [ ] **Step 4: Create the streamer**

```csharp
using System.Threading.Channels;
using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Drains a producer's audio and transcript streams together, turning the transcript chunks into
/// diffs whose offsets are validated against the audio actually ingested.
/// </summary>
public sealed class ExternalTranscriptStreamer(IServiceProvider services)
{
    private IServiceProvider Services { get; } = services;
    private ILogger Log => field ??= Services.LogFor(GetType());

    public IAsyncEnumerable<TranscriptDiff> ToDiffs(
        IAsyncEnumerable<ExternalTranscriptChunk> chunks,
        Func<TimeSpan> getIngestedDuration,
        ExternalTranscriptBuilder builder,
        CancellationToken cancellationToken)
        => Impl(chunks, getIngestedDuration, builder, cancellationToken);

    // Private methods

    private static async IAsyncEnumerable<TranscriptDiff> Impl(
        IAsyncEnumerable<ExternalTranscriptChunk> chunks,
        Func<TimeSpan> getIngestedDuration,
        ExternalTranscriptBuilder builder,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var chunk in chunks.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var diff = builder.Append(chunk, getIngestedDuration.Invoke());
            if (diff is not null)
                yield return diff;
        }
    }
}
```

- [ ] **Step 5: Implement the frontend method**

In `LiveAudioStreams.cs`, mirroring `PushStream`'s guards and adding maintenance checks on both streams:

```csharp
    public async Task<ChatEntry> PushAudioWithTranscript(
        Session session,
        string chatId,
        string? repliedChatEntryId,
        double clientStartAt,
        int preSkip,
        RpcStream<AudioFrame> frameStream,
        RpcStream<ExternalTranscriptChunk> transcriptStream,
        CancellationToken cancellationToken)
    {
        var stopCts = new CancellationTokenSource(Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(5));
        try {
            var chatIdTyped = ChatId.Parse(chatId);
            var repliedEntryIdTyped = ChatEntryId.ParseNullable(repliedChatEntryId);
            var maintenances = Services.GetRequiredService<IMaintenancesBackend>();
            await maintenances.RequireAvailable(chatIdTyped, cancellationToken).ConfigureAwait(false);

            var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
            var audioRecord = new AudioRecord(
                streamId, session, chatIdTyped, clientStartAt, repliedEntryIdTyped);
            var checkedFrames = frameStream.RequireAvailable(maintenances, chatIdTyped, stopCts.Token);
            var checkedChunks = transcriptStream.RequireAvailable(maintenances, chatIdTyped, stopCts.Token);
            return await Backend
                .ProcessAudioWithTranscript(
                    audioRecord, preSkip,
                    RpcStream.New(checkedFrames), RpcStream.New(checkedChunks), stopCts.Token)
                .ConfigureAwait(false);
        }
        finally {
            frameStream.Disconnect();
            transcriptStream.Disconnect();
            stopCts.CancelAndDisposeSilently();
        }
    }
```

Add the matching `ProcessAudioWithTranscript` to `IAudioStreamingBackend` and implement it in `AudioStreamingBackend.ProcessAudio.cs` as a call to `ProcessAudioInternal` whose `TranscriptSource` is `ExternalTranscriptStreamer.ToDiffs`, with `getIngestedDuration` reading the segment's frame count × `Constants.Audio.OpusFrameDuration`, and finalizing via `builder.Finalize(segmentDuration)`.

- [ ] **Step 6: Register the streamer and run the test**

Add `services.AddSingleton<ExternalTranscriptStreamer>();` to `StreamingServiceModule` beside the other non-client services.

Run: `dotnet test tests/Streaming.IntegrationTests --filter ShouldFinalizeAPlayableEntryFromExternalAudioAndText`
Expected: PASS.

- [ ] **Step 7: Write the audio-without-text test (Review Focus #2)**

```csharp
    [Fact]
    public async Task ShouldFinalizeAPlayableEntryWhenNoTextArrives()
    {
        // A bot that speaks but sends no transcript still produced audio someone can play.

        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var frames = await TestAudio.ReadFrames("test-audio-1.wav");

        // act
        var entry = await Tester.LiveAudioStreams.PushAudioWithTranscript(
            Tester.Session, chatId.Value, null, Tester.Clocks().SystemClock.Now.EpochOffset.TotalSeconds,
            0,
            RpcStream.New(frames.ToAsyncEnumerable()),
            RpcStream.New(AsyncEnumerable.Empty<ExternalTranscriptChunk>()),
            CancellationToken.None);

        // assert
        entry.Should().NotBeNull();
        entry.Audio.Should().NotBeNull("the audio is the message even with no words");
        entry.IsContentStreaming.Should().BeFalse();
    }
```

- [ ] **Step 8: Run it and fix what it finds**

Run: `dotnet test tests/Streaming.IntegrationTests --filter ShouldFinalizeAPlayableEntryWhenNoTextArrives`
Expected: PASS. If finalization throws on an empty transcript, guard `Finalize` — it already returns early when `Text.Length == 0`; the failure would be further down, in entry finalization requiring non-empty content.

- [ ] **Step 9: Write the either-stream-ends-first tests**

```csharp
    [Fact]
    public async Task ShouldFinalizeWhenTheTranscriptEndsBeforeTheAudio()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var frames = await TestAudio.ReadFrames("test-audio-1.wav");

        // act - one chunk, then the transcript stream closes while frames keep coming
        var entry = await Tester.LiveAudioStreams.PushAudioWithTranscript(
            Tester.Session, chatId.Value, null, Tester.Clocks().SystemClock.Now.EpochOffset.TotalSeconds,
            0,
            RpcStream.New(frames.ToAsyncEnumerable()),
            RpcStream.New(new[] { new ExternalTranscriptChunk("Only this", true, 0.2, true) }
                .ToAsyncEnumerable()),
            CancellationToken.None);

        // assert
        entry.Content.Should().Be("Only this");
        entry.Audio.Should().NotBeNull();
    }
```

- [ ] **Step 10: Run the whole suite and commit**

Run: `dotnet test tests/Streaming.IntegrationTests --filter ExternalAudioTranscriptStreamTest`
Expected: PASS, 3 tests.

```bash
git add src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs src/dotnet/Streaming.Service tests/Streaming.IntegrationTests/ExternalAudioTranscriptStreamTest.cs
git commit -m "feat(streaming): accept producer-supplied audio with its own transcript"
```

---

### Task 5: The MCP voice lease

Mirrors `ChatEntryStreams` exactly — a node-pinned lease whose `StreamId` routes later calls home — but its appends carry Ogg Opus bytes as well as text.

**Files:**
- Create: `src/dotnet/Api/Chat/ChatVoiceStream.cs`
- Create: `src/dotnet/Chat.Contracts/IChatVoiceStreamsBackend.cs`
- Create: `src/dotnet/Chat.Service/ChatVoiceStreams.cs`
- Create: `src/dotnet/Mcp/Models/McpVoiceStream.cs`
- Modify: `src/dotnet/Mcp/Tools/McpMessageTools.cs`
- Modify: `src/dotnet/Mcp/Models/McpModelExt.cs`
- Modify: `src/dotnet/Chat.Service/Module/ChatServiceModule.cs`
- Modify: `src/dotnet/Api/Constants.cs`
- Modify: `tests/Mcp.IntegrationTests/McpToolSchemaTest.cs`
- Test: `tests/Mcp.IntegrationTests/McpVoiceStreamToolsTest.cs`

**Interfaces:**
- Consumes: `PushAudioWithTranscript` machinery (Task 4), `OggOpusReader` (existing).
- Produces: `ChatVoiceStream(StreamId Id, ChatEntryId EntryId, int TextOffset, long AudioBytes, bool IsCompleted)`.

- [ ] **Step 1: Write the failing split-page test (Review Focus #1)**

```csharp
using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public class McpVoiceStreamToolsTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ShouldReassembleAnOggPageSplitAcrossTwoAppends()
    {
        // A producer chunks at arbitrary byte boundaries; a frame straddling two appends must
        // survive intact rather than being dropped or corrupted.

        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();
        var ogg = await TestAudio.ReadOggOpusBytes("test-audio-1.opuss");
        var half = ogg.Length / 2;

        // act
        var stream = await CallTool<McpVoiceStream>(client, "start_voice_stream",
            new { chatId = chatId.Value });
        await CallTool<McpVoiceStream>(client, "append_voice_stream", new {
            streamId = stream.StreamId, textOffset = 0,
            audioBase64 = Convert.ToBase64String(ogg[..half]),
        });
        await CallTool<McpVoiceStream>(client, "append_voice_stream", new {
            streamId = stream.StreamId, textOffset = 0,
            audioBase64 = Convert.ToBase64String(ogg[half..]),
        });
        var final = await CallTool<McpVoiceStream>(client, "finish_voice_stream",
            new { streamId = stream.StreamId });

        // assert
        var entry = await Tester.Chats.GetEntry(
            Tester.Session, ChatEntryId.New(chatId, final.EntryId));
        entry!.Audio.Should().NotBeNull();
        entry.Audio!.Duration.Should().BeGreaterThan(TimeSpan.Zero,
            "both halves must contribute frames");
    }
}
```

`TestAudio.ReadOggOpusBytes` may not exist — if not, add it to the test-audio helper reading `lib/data/test-audio-1.opuss` as raw bytes.

- [ ] **Step 2: Run and verify it fails**

Run: `dotnet test tests/Mcp.IntegrationTests --filter McpVoiceStreamToolsTest`
Expected: FAIL — `start_voice_stream` is not a tool.

- [ ] **Step 3: Add the constants**

In `Constants.Chat`, beside the entry-stream constants:

```csharp
        // An Ogg Opus utterance at ~32 kbps; well above any plausible single message
        public const int MaxVoiceStreamAudioBytes = 16 * 1024 * 1024;
        public const int MaxVoiceStreamChunkBytes = 1024 * 1024;
```

- [ ] **Step 4: Add the result model and its MCP shape**

`src/dotnet/Api/Chat/ChatVoiceStream.cs`:

```csharp
namespace ActualChat.Chat;

/// <summary>
/// A voice entry being pushed in call by call: the handle to send more, and how much text and
/// audio the server has accepted.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ChatVoiceStream(
    [property: DataMember(Order = 0), Key(0)] StreamId Id,
    [property: DataMember(Order = 1), Key(1)] ChatEntryId EntryId,
    [property: DataMember(Order = 2), Key(2)] int TextOffset,
    [property: DataMember(Order = 3), Key(3)] long AudioBytes,
    [property: DataMember(Order = 4), Key(4)] bool IsCompleted);
```

`src/dotnet/Mcp/Models/McpVoiceStream.cs`:

```csharp
namespace ActualChat.Mcp;

public sealed record McpVoiceStream(
    string StreamId,
    long EntryId,
    int TextOffset,
    long AudioBytes,
    bool IsFinished);
```

And in `McpModelExt`, beside the other `ToMcpModel` overloads:

```csharp
    public static McpVoiceStream ToMcpModel(this ChatVoiceStream stream)
        => new(stream.Id.Value, stream.EntryId.LocalId, stream.TextOffset,
            stream.AudioBytes, stream.IsCompleted);
```

- [ ] **Step 5: Add the backend contract**

`src/dotnet/Chat.Contracts/IChatVoiceStreamsBackend.cs` — identical routing rationale to `IChatEntryStreamsBackend`:

```csharp
using ActualChat.Attributes;
using ActualChat.Sharding;
using ActualLab.Rpc;

namespace ActualChat.Chat;

// Distributed rather than the assembly's Server default: the lease holds an open audio and
// transcript stream in one node's memory, and StreamId's NodeRef routes later calls back there.

/// <summary>
/// Backend for voice entries pushed call by call, for producers that cannot hold an
/// <see cref="RpcStream{T}"/> open.
/// </summary>
[BackendService(nameof(HostRole.ChatBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(ShardScheme.ChatBackend))]
public interface IChatVoiceStreamsBackend : IComputeService, IBackendService
{
    Task<ChatVoiceStream> Start(
        ChatId chatId,
        AuthorId authorId,
        UserId userId,
        long? repliedEntryLid,
        CancellationToken cancellationToken);

    Task<ChatVoiceStream> Append(
        StreamId streamId,
        UserId userId,
        int textOffset,
        string? text,
        byte[]? audio,
        double? audioOffset,
        CancellationToken cancellationToken);

    Task<ChatVoiceStream> Finish(StreamId streamId, UserId userId, CancellationToken cancellationToken);
}
```

- [ ] **Step 6: Implement the lease**

`src/dotnet/Chat.Service/ChatVoiceStreams.cs` follows `ChatEntryStreams` structurally: a
`ConcurrentDictionary<Symbol, ExpiringEntry<Symbol, Lease>>`, a settable `IdleTimeout` defaulting
to `Constants.Chat.EntryStreamIdleTimeout`, ownership by `UserId`, offset-checked text appends, and
a disposer that completes both channels so an abandoned stream finalizes.

The lease holds two channels — `Channel<AudioFrame>` and `Channel<ExternalTranscriptChunk>` — and
one `OggOpusReader`. `Append` decodes audio with the reader's incremental API, which is what makes
Review Focus #1 pass:

```csharp
            if (audio is { Length: > 0 }) {
                if (audio.Length > Constants.Chat.MaxVoiceStreamChunkBytes)
                    throw StandardError.Constraint("Audio chunk is too large.");
                if (lease.AudioBytes + audio.Length > Constants.Chat.MaxVoiceStreamAudioBytes)
                    throw StandardError.Constraint("This voice stream is too long.");

                // Append/TryRead keeps a frame that straddles two chunks: the reader buffers the
                // partial packet until the bytes completing it arrive
                lease.OggReader.Append(audio);
                while (lease.OggReader.TryRead(out var frame))
                    lease.Frames.Writer.TryWrite(frame);
                lease.AudioBytes += audio.Length;
            }
```

`Start` launches `ProcessAudioWithTranscript` (Task 4) over the two channel readers and captures the
created entry the way `ChatEntryStreams.Start` does.

- [ ] **Step 7: Add the MCP tools**

In `McpMessageTools.cs`, after the message-stream tools:

```csharp
    [McpServerTool(Name = "start_voice_stream", UseStructuredContent = true)]
    [Description("Opens a voice message you fill in as you speak it: listeners hear it live and it " +
        "settles into an ordinary playable message. Send Ogg Opus audio with append_voice_stream, " +
        "optionally with the matching text, then finish_voice_stream. Send audio before the text it " +
        "corresponds to, or pass audioOffset, so the transcript lines up with the sound.")]
    public async Task<McpVoiceStream> StartVoiceStream(
        [Description("The chat id.")] string chatId,
        [Description("LID of the message this one replies to.")] long? replyToId = null,
        CancellationToken cancellationToken = default)
    {
        var stream = await Chats
            .StartVoiceStream(Session, ChatId.Parse(chatId), replyToId, cancellationToken)
            .ConfigureAwait(false);
        return stream.ToMcpModel();
    }

    [McpServerTool(Name = "append_voice_stream", UseStructuredContent = true)]
    [Description("Appends Ogg Opus audio, text, or both. `textOffset` is the number of characters " +
        "the server already has; a mismatch writes no text and returns the server's offset so a " +
        "retried call can resume. Audio is append-only. `audioOffset` is seconds into your own " +
        "audio at the end of this text; omit it and the server uses the audio it has received.")]
    public async Task<McpVoiceStream> AppendVoiceStream(
        [Description("Stream id from start_voice_stream.")] string streamId,
        [Description("Character offset this text starts at.")] int textOffset,
        [Description("Text to append.")] string? text = null,
        [Description("Ogg Opus bytes, base64-encoded; at most 1 MB decoded.")] string? audioBase64 = null,
        [Description("Seconds into your audio at the end of this text.")] double? audioOffset = null,
        CancellationToken cancellationToken = default)
    {
        var audio = audioBase64.IsNullOrEmpty() ? null : Convert.FromBase64String(audioBase64);
        var stream = await Chats
            .AppendVoiceStream(Session, StreamId.Parse(streamId), textOffset, text, audio,
                audioOffset, cancellationToken)
            .ConfigureAwait(false);
        return stream.ToMcpModel();
    }

    [McpServerTool(Name = "finish_voice_stream", UseStructuredContent = true)]
    [Description("Closes the stream and settles the voice message on what was received. Calling it " +
        "again within 90 seconds returns the same result.")]
    public async Task<McpVoiceStream> FinishVoiceStream(
        [Description("Stream id from start_voice_stream.")] string streamId,
        CancellationToken cancellationToken = default)
    {
        var stream = await Chats
            .FinishVoiceStream(Session, StreamId.Parse(streamId), cancellationToken)
            .ConfigureAwait(false);
        return stream.ToMcpModel();
    }
```

Add the frontend `StartVoiceStream` / `AppendVoiceStream` / `FinishVoiceStream` to `IChats` and
`Chats`, resolving the author and account exactly as `StartEntryStream` does.

- [ ] **Step 8: Update the tool schema test**

In `McpToolSchemaTest.ExpectedTools`, after the message-stream entries:

```csharp
        "start_voice_stream", "append_voice_stream", "finish_voice_stream",
```

- [ ] **Step 9: Run the split-page test**

Run: `dotnet test tests/Mcp.IntegrationTests --filter ShouldReassembleAnOggPageSplitAcrossTwoAppends`
Expected: PASS.

- [ ] **Step 10: Write the lifecycle tests**

Mirror `McpMessageToolsTest`'s four: text and audio assembled across calls, a retried append
reporting the server's text offset without writing twice, another user's stream refused, and a
maintained chat refusing `start_voice_stream`. Add one for a corrupt chunk:

```csharp
    [Fact]
    public async Task ShouldRejectACorruptAudioChunkAndKeepTheStreamOpen()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();
        var stream = await CallTool<McpVoiceStream>(client, "start_voice_stream",
            new { chatId = chatId.Value });

        // act
        await CallToolExpectingError(client, "append_voice_stream", new {
            streamId = stream.StreamId, textOffset = 0, audioBase64 = "bm90IG9nZw==",
        });
        var after = await CallTool<McpVoiceStream>(client, "append_voice_stream", new {
            streamId = stream.StreamId, textOffset = 0, text = "still here",
        });

        // assert
        after.TextOffset.Should().Be("still here".Length,
            "a bad chunk must not kill the stream");
        await CallTool<McpVoiceStream>(client, "finish_voice_stream", new { streamId = stream.StreamId });
    }
```

- [ ] **Step 11: Run the suite and commit**

Run: `dotnet test tests/Mcp.IntegrationTests --filter McpVoiceStreamToolsTest`
Then: `dotnet test tests/Mcp.IntegrationTests --filter McpToolSchemaTest`
Expected: both PASS.

```bash
git add src/dotnet/Api/Chat/ChatVoiceStream.cs src/dotnet/Chat.Contracts/IChatVoiceStreamsBackend.cs src/dotnet/Chat.Service src/dotnet/Mcp tests/Mcp.IntegrationTests
git commit -m "feat(mcp): expose a voice-stream lifecycle carrying Ogg Opus"
```

---

### Task 6: Speak mode — the same-language dub

Independent of Tasks 1–5: it needs only the text path already on `dev`.

**Files:**
- Modify: `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.Dubbing.cs`
- Test: `tests/Streaming.IntegrationTests/SpeakModeTest.cs`

**Interfaces:**
- Consumes: `EnsureDub`, `RunDub`, `VoiceOverMix`, `SpeakerVoices`, `ISpeechSynthesizer` (existing).
- Produces: no new public surface — speak mode is selected by the dub stream's language.

- [ ] **Step 1: Write the failing speak test**

```csharp
    [Fact]
    public async Task ShouldSpeakATextOnlyEntryInItsOwnLanguage()
    {
        // arrange - a bot streams text, no audio
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var stream = await Tester.Chats.StartEntryStream(Tester.Session, chatId, null, default);
        await Tester.Chats.AppendEntryStream(Tester.Session, stream.Id, 0, "Hello there", default);
        var entry = await Tester.Chats.FinishEntryStream(Tester.Session, stream.Id, default);
        var current = await ChatsBackend.GetEntry(entry.EntryId, default);

        // act - ask for the entry's own language, which selects speak mode
        var dubStreamId = StreamId.New(StreamId.Parse(current!.ContentStreamId), Language.English);
        var audio = await StreamingBackend.GetAudio(dubStreamId, TimeSpan.Zero, CancellationToken.None);

        // assert
        audio.Should().NotBeNull("a text-only entry must be speakable");
    }
```

- [ ] **Step 2: Run and verify it fails**

Run: `dotnet test tests/Streaming.IntegrationTests --filter ShouldSpeakATextOnlyEntryInItsOwnLanguage`
Expected: FAIL — `RunDub` waits for a translation that never arrives, or `DecideOnSource` declines.

- [ ] **Step 3: Add the mode discriminator**

In `RunDub`, right after `language` is read:

```csharp
            // Same language means "speak this", not "translate it": there is nothing to translate,
            // and DubStabilizer exists only to decide whether a translation is warranted
            var isSpeakMode = await IsSpeakMode(sourceStreamId, language, cancellationToken)
                .ConfigureAwait(false);
```

`IsSpeakMode` compares `language` against the source entry's language (via `ChatsBackend.GetEntry`
on the entry owning `sourceStreamId`, falling back to the chat's default language) and returns true
when they match **and the source entry has no audio of its own**.

- [ ] **Step 4: Branch past the translation machinery**

Where `RunDub` chooses its text source, use the source transcript directly in speak mode instead of
`WaitForTranslation`, and skip `DecideOnSource` entirely:

```csharp
            var decision = isSpeakMode
                ? DubDecision.Dub
                : await DecideOnSource(sourceMemoizer, language, cancellationToken).ConfigureAwait(false);
            var textMemoizer = isSpeakMode
                ? sourceMemoizer
                : await WaitForTranslation(dubStreamId, cancellationToken).ConfigureAwait(false);
```

Use the local names `RunDub` already has for the source memoizer and the translation wait; read the
method before editing rather than assuming these.

- [ ] **Step 5: Run the test**

Run: `dotnet test tests/Streaming.IntegrationTests --filter ShouldSpeakATextOnlyEntryInItsOwnLanguage`
Expected: PASS. `FakeSpeechSynthesizer` supplies the audio, so no vendor is involved.

- [ ] **Step 6: Assert the voice comes from the author**

```csharp
    [Fact]
    public async Task ShouldSpeakInTheAuthorsVoice()
    {
        // arrange, act as above, then
        var voices = AppHost.Services.GetRequiredService<SpeakerVoices>();
        var expected = await voices.Get(chatId, current.AuthorId, CancellationToken.None);

        // assert
        FakeSpeechSynthesizer.LastVoiceId.Should().Be(expected);
    }
```

If `FakeSpeechSynthesizer` records no last voice, add a settable `LastVoiceId` to it — it is a test
double and that is what it is for.

- [ ] **Step 7: Write the never-speak-over-real-audio test (Review Focus #3)**

```csharp
    [Fact]
    public async Task ShouldNotSpeakAnEntryThatAlreadyHasAudio()
    {
        // A human voice message must never be synthesized over.

        // arrange - a real recording
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.RecordTestAudio(chatId);

        // act
        var dubStreamId = StreamId.New(StreamId.Parse(entry.Audio!.StreamId), Language.English);
        var audio = await StreamingBackend.GetAudio(dubStreamId, TimeSpan.Zero, CancellationToken.None);

        // assert - the original, not a synthesis
        FakeSpeechSynthesizer.SynthesizeCallCount.Should().Be(0,
            "an entry with its own audio is never spoken for");
    }
```

- [ ] **Step 8: Run the suite and commit**

Run: `dotnet test tests/Streaming.IntegrationTests --filter SpeakModeTest`
Expected: PASS, 3 tests.

```bash
git add src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.Dubbing.cs tests/Streaming.IntegrationTests/SpeakModeTest.cs
git commit -m "feat(streaming): speak a text-only entry as a same-language dub"
```

---

### Task 7: The listener gate

**Files:**
- Modify: `src/dotnet/Users.Contracts` — the user setting holding dub/voice preferences
- Modify: `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs`
- Modify: `src/dotnet/UI.Blazor.App/Components/Settings/` — the Voice & Transcription tab
- Test: `tests/Streaming.IntegrationTests/SpeakModeTest.cs`

**Interfaces:**
- Consumes: speak mode (Task 6).
- Produces: `UserLanguageSettings.IsBotSpeechEnabled` (bool, default false).

- [ ] **Step 1: Write the failing gate test**

```csharp
    [Fact]
    public async Task ShouldNotSynthesizeForAListenerWhoHasNotOptedIn()
    {
        // arrange - a bot text entry, and a listener with the default setting
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await StreamBotText(chatId, "Hello there");

        // act
        var dubStreamId = StreamId.New(StreamId.Parse(entry.ContentStreamId), Language.English);
        var audio = await Tester.LiveAudioStreams.GetStream(
            Tester.Session, dubStreamId.Value, TimeSpan.Zero, CancellationToken.None);

        // assert
        audio.Should().BeNull("bot speech is off by default");
        FakeSpeechSynthesizer.SynthesizeCallCount.Should().Be(0,
            "an un-opted-in listener must not cost a synthesis");
    }
```

- [ ] **Step 2: Run and verify it fails**

Run: `dotnet test tests/Streaming.IntegrationTests --filter ShouldNotSynthesizeForAListenerWhoHasNotOptedIn`
Expected: FAIL — audio is returned and the synthesizer ran.

- [ ] **Step 3: Add the setting**

Add `IsBotSpeechEnabled` (default `false`) to the user settings record holding `DubVoice`, with a
new `[DataMember(Order = N), Key(N)]` **appended** after the highest existing key.

- [ ] **Step 4: Enforce it in the frontend**

In `LiveAudioStreams.GetStream`, before resolving a dub: when the requested stream is a
same-language dub of an entry whose `IsViaApi` is true, read the caller's setting and return `null`
if it is off. Doing it here, not in the backend, is what keeps the synthesis from ever starting.

- [ ] **Step 5: Run the test**

Run: `dotnet test tests/Streaming.IntegrationTests --filter ShouldNotSynthesizeForAListenerWhoHasNotOptedIn`
Expected: PASS.

- [ ] **Step 6: Write the opted-in test**

```csharp
    [Fact]
    public async Task ShouldSynthesizeForAListenerWhoOptedIn()
    {
        // arrange - same as above, with the setting on
        await SetBotSpeechEnabled(Tester, true);
        // ... act as above

        // assert
        audio.Should().NotBeNull();
    }
```

- [ ] **Step 7: Add the settings toggle**

Add a switch to the Voice & Transcription settings tab beside the dub-voice picker, labelled from
the localized catalog (per `docs/i18n.md` — no hard-coded English). It reads "Read API-written
messages aloud", matching what `IsViaApi` actually means.

- [ ] **Step 8: Run, build the UI, commit**

Run: `dotnet test tests/Streaming.IntegrationTests --filter SpeakModeTest`
Run: `dotnet build src/dotnet/UI.Blazor.App`
Expected: tests PASS, 5 tests; 0 build errors.

```bash
git add src/dotnet/Users.Contracts src/dotnet/Streaming.Service src/dotnet/UI.Blazor.App tests/Streaming.IntegrationTests
git commit -m "feat(streaming): gate bot speech behind a listener setting"
```

---

### Task 8: Speak mode on replay

Without this the feature works live and vanishes from history.

**Files:**
- Modify: `src/dotnet/Streaming.Service/Services/ReplayDubs.cs`
- Test: `tests/Streaming.IntegrationTests/SpeakModeTest.cs`

**Interfaces:**
- Consumes: speak mode (Task 6), the gate (Task 7).

- [ ] **Step 1: Write the failing replay test**

```csharp
    [Fact]
    public async Task ShouldSpeakAFinishedEntryOnReplay()
    {
        // arrange - a finished bot entry, opted in
        await SetBotSpeechEnabled(Tester, true);
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await StreamBotText(chatId, "Hello from history");

        // act - replay, not live
        var audio = await ReplayDubs.Get(entry.Id, Language.English, CancellationToken.None);

        // assert
        audio.Should().NotBeNull("a bot message must still be speakable after it settles");
    }
```

Use `ReplayDubs`' actual accessor name; read the file first.

- [ ] **Step 2: Run and verify it fails**

Run: `dotnet test tests/Streaming.IntegrationTests --filter ShouldSpeakAFinishedEntryOnReplay`
Expected: FAIL.

- [ ] **Step 3: Apply the same discriminator**

`ReplayDubs` selects its text source the same way `RunDub` does; give it the same speak-mode branch,
calling the shared `IsSpeakMode` helper from Task 6 rather than duplicating the condition.

- [ ] **Step 4: Run and commit**

Run: `dotnet test tests/Streaming.IntegrationTests --filter SpeakModeTest`
Expected: PASS, 6 tests.

```bash
git add src/dotnet/Streaming.Service/Services/ReplayDubs.cs tests/Streaming.IntegrationTests/SpeakModeTest.cs
git commit -m "feat(streaming): speak bot entries on replay too"
```

---

### Task 9: Documentation

**Files:**
- Modify: `docs/integrations/streaming-writes.md`
- Modify: `~/projects/marketing/product/features.md` (separate repo — leave uncommitted)

- [ ] **Step 1: Add the voice section**

Extend `docs/integrations/streaming-writes.md` with a "Speaking" section covering: the two audio
transports and when to use each; the Ogg Opus requirement and why; the offset rules including the
audio-before-text guidance and what happens without offsets; the 90 s idle and 30 min caps; that
real bot audio plays for everyone while synthesized speech is opt-in; and a runnable example that
streams an LLM response through a TTS into a chat, matching the register of the existing example.

- [ ] **Step 2: Verify the example's syntax**

Run the same extraction check used for the existing example:

```bash
python3 - <<'EOF'
import re, py_compile, tempfile, os
doc = open('docs/integrations/streaming-writes.md', encoding='utf-8').read()
for i, code in enumerate(re.findall(r'```python\n(.*?)```', doc, re.S)):
    f = tempfile.NamedTemporaryFile('w', suffix='.py', delete=False, encoding='utf-8')
    f.write(code); f.close()
    py_compile.compile(f.name, doraise=True); os.unlink(f.name)
    print(f"example {i+1}: syntax OK")
EOF
```

- [ ] **Step 3: Update the marketing row**

In `~/projects/marketing/product/features.md`, add a row for speaking bots beside the streaming
writes row. Leave it uncommitted for review, as with the streaming-writes row.

- [ ] **Step 4: Commit the docs**

```bash
git add docs/integrations/streaming-writes.md
git commit -m "docs(integrations): document speaking bots"
```

---

## Done when

1. A bot holding only an API key can speak in a chat over MCP, with its own voice, and listeners
   hear it live.
2. The same is possible over RPC for a client that can hold a stream open.
3. The result is an ordinary playable entry: scrubbable, replayable, searchable.
4. A bot that sends only text is read aloud to listeners who asked for that, and silent to everyone
   else, costing one synthesis per entry no matter how many listen.
5. Native `PushStream` behaviour is unchanged, proven by its existing tests.
