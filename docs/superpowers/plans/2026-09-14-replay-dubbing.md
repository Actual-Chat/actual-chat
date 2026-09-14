# Replay Dubbing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A listener with "Translated voice" on hears replayed voice entries spoken in their language (stock voice) instead of the original.

**Architecture:** The stored `Translation` per (entry, language) owns a synthesized dub media (`DubMediaId` + the content hash it was made from). `ReplayDubs` (Streaming.Service) synthesizes it lazily via Soniox REST TTS → unpaced `OpusFramePump` → webm blob + `MediaFull`, and `ReplayStreamMuxer` swaps the entry's blob for the dub's, stretching the timeline so dubs never overlap. The client passes the dub language it already computes for live.

**Tech Stack:** .NET 11, ActualLab.Fusion (compute services, commands), EF Core migrations (`ef-migrations.cmd`), MessagePack array-form records (append-only keys), OpusSharp, xUnit + FluentAssertions, Moq.

**Spec:** `docs/superpowers/specs/2026-09-14-replay-dubbing-design.md`

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing C#: no `Async` suffix, no XML docs on members, mixed brace style, comments only where the code can't say it.
- MessagePack array-form types: append keys only, never renumber. `Translation` gets keys 7 and 8; nothing else changes numbering.
- The old RPC overloads (`GetReplayStream` 6-arg) stay for old clients; new ones are additions.
- Failures on the dub path always fall back to the original audio; replay must never break because dubbing did.
- Never push; commit locally only. Commit trailer:
  `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>` and
  `Claude-Session: https://claude.ai/code/session_01A697SeSrV5La5UsZE9oFRE`.
- Build/test by project: `dotnet build tests/<Project>/<Project>.csproj` then `dotnet test --no-build tests/<Project>/<Project>.csproj --filter "FullyQualifiedName~<Test>"`. Integration test hosts take minutes to build; that is not a hang.
- Branch `feat/voice-dubbing` is linked to issue #4496 (`git config branch.feat/voice-dubbing.issue`); no `/track-issue` run is needed.
- Deviations from the spec, decided while planning (the doc task records them): (a) the replay player reads the dub language once when replay starts — `ReplayStreamProcessor` has no break/reconnect loop, so a toggle mid-replay applies on the next start; (b) the dub service is a plain in-process `ReplayDubs` singleton rather than an RPC `IDubsBackend` — it only composes existing backends, and the muxer that needs it runs on the same host; (c) a dub media whose blob is missing falls back to the original for that entry but is not cleared from the translation — the media record's absence (not the blob's) is what triggers regeneration.

## File Structure

| File | Responsibility |
|---|---|
| `src/dotnet/Api/Chat/Translation.cs` | `DubMediaId`/`DubContentHash` on `Translation` + `TranslationDiff`; `HasValidDub` |
| `src/dotnet/Chat.Service/Db/DbTranslation.cs`, `src/dotnet/Chat.Service.Migration/Migrations/*` | columns + migration |
| `src/dotnet/Chat.Service/TranslationsBackend.cs` | clear the dub when content changes; delete the orphaned media |
| `src/dotnet/Transcription.Contracts/ISpeechSynthesizer.cs` | one-shot `Synthesize(text)` overload |
| `src/dotnet/Transcription.Service/Synthesis/OpusFramePump.cs` | optional pacing |
| `src/dotnet/Transcription.Service/Synthesis/SpeechSynthesizerExt.cs` | shared "PCM producer → AudioSource" plumbing for one-shot synthesis |
| `src/dotnet/Transcription.Service/Transcribers/SonioxTtsClient.cs` | `Generate` (REST, whole text → PCM) |
| `src/dotnet/Transcription.Service/Synthesis/{Soniox,Fake}SpeechSynthesizer.cs`, `tests/Testing.Host/RecordingSpeechSynthesizer.cs` | one-shot implementations |
| `src/dotnet/Streaming.Service/Services/AudioSegmentSaver.cs` | `SaveAndCreateMedia(AudioSource, blobId, chatId, ct)` |
| `src/dotnet/Streaming.Service/Services/ReplayDubs.cs` | get-or-create a dub media for (entry, language) |
| `src/dotnet/Api/Constants.Audio.cs` | `ReplayDubTimeout`, `ReplayDubLookahead` |
| `src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs`, `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs` | 7-arg `GetReplayStream` |
| `src/dotnet/Streaming.Service/Services/ReplayStreamMuxer.cs` | dub substitution, timeline stretch, lookahead, skipTo scaling |
| `src/dotnet/UI.Blazor.App/Services/Audio/ReplayStreamProcessor.cs`, `.../Playback/ChatReplayPlayer.cs` | client wiring |
| `docs/live-audio/12-dubbing.md` | replay section |

---

### Task 1: The translation owns its dub

**Files:**
- Modify: `src/dotnet/Api/Chat/Translation.cs`
- Modify: `src/dotnet/Chat.Service/Db/DbTranslation.cs`
- Modify: `src/dotnet/Chat.Service/TranslationsBackend.cs:105-185` (`OnChange`)
- Create: `src/dotnet/Chat.Service.Migration/Migrations/<timestamp>_Add_Translation_Dub.cs` (+ `.Designer.cs`, snapshot update — generated)
- Test: `tests/Chat.UnitTests/TranslationDubTest.cs`, `tests/Chat.IntegrationTests/TranslationDubTest.cs`

**Interfaces:**
- Produces: `Translation.DubMediaId` (`MediaId?`, key 7), `Translation.DubContentHash` (`HashString`, key 8), `Translation.HasValidDub` (`bool`, not serialized), `TranslationDiff.DubMediaId` (`Option<MediaId?>`), `TranslationDiff.DubContentHash` (`HashString?`).

- [ ] **Step 1: Write the failing unit test**

`tests/Chat.UnitTests/TranslationDubTest.cs`:

```csharp
using ActualChat.Chat;

namespace ActualChat.Chat.UnitTests;

public class TranslationDubTest
{
    private static readonly TranslationId Id =
        TranslationId.New(ChatEntryId.New(new ChatId("p-Nb5srp-pKGsAk"), 590), Languages.English);

    [Fact]
    public void ADubMadeFromTheCurrentContentIsValid()
    {
        var translation = new Translation(Id) {
            Content = "Hello",
            SourceContentHash = ChatEntryHashExt.GetContentHashString("Привет"),
            DubMediaId = new MediaId("p-Nb5srp-pKGsAk:dub1"),
            DubContentHash = ChatEntryHashExt.GetContentHashString("Hello"),
        };
        translation.HasValidDub.Should().BeTrue();
    }

    [Fact]
    public void ADubMadeFromOlderContentIsNotValid()
    {
        var translation = new Translation(Id) {
            Content = "Hello there",
            SourceContentHash = ChatEntryHashExt.GetContentHashString("Привет"),
            DubMediaId = new MediaId("p-Nb5srp-pKGsAk:dub1"),
            DubContentHash = ChatEntryHashExt.GetContentHashString("Hello"),
        };
        translation.HasValidDub.Should().BeFalse();
    }

    [Fact]
    public void NoMediaMeansNoDub()
    {
        var translation = new Translation(Id) { Content = "Hello" };
        translation.HasValidDub.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet build tests/Chat.UnitTests/Chat.UnitTests.csproj 2>&1 | grep -E " error |Build succeeded"`
Expected: `error CS0117`/`CS1061` for `DubMediaId`, `DubContentHash`, `HasValidDub`.

- [ ] **Step 3: Add the fields to `Translation` and `TranslationDiff`**

In `src/dotnet/Api/Chat/Translation.cs`, after `[DataMember, Key(6)] public StreamId? StreamId { get; set; }`:

```csharp
    [DataMember, Key(7)] public MediaId? DubMediaId { get; init; }
    [DataMember, Key(8)] public HashString DubContentHash { get; init; }
```

After `IsStreaming`:

```csharp
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool HasValidDub
        => DubMediaId != null && DubContentHash == ChatEntryHashExt.GetContentHashString(Content);
```

(`ChatEntryHashExt` lives in `Chat.Contracts`; if `Api` can't reference it, compute the hash the same way inline: `Content.Hash().Blake3().ToBlake3Base64HashString()` — check `src/dotnet/Chat.Contracts/ChatEntryHashExt.cs:13`. `Api` is referenced by `Chat.Contracts`, so the inline form is the one to use here; keep the test using `ChatEntryHashExt` — both produce the same string.)

In `TranslationDiff`, after `StreamId`:

```csharp
    [DataMember] public Option<MediaId?> DubMediaId { get; init; }
    [DataMember] public HashString? DubContentHash { get; init; }
```

- [ ] **Step 4: Run the unit test**

Run: `dotnet build tests/Chat.UnitTests/Chat.UnitTests.csproj && dotnet test --no-build tests/Chat.UnitTests/Chat.UnitTests.csproj --filter "FullyQualifiedName~TranslationDubTest"`
Expected: 3 passed.

- [ ] **Step 5: Persist the fields**

`src/dotnet/Chat.Service/Db/DbTranslation.cs` — add after `StreamId`:

```csharp
    public string? DubMediaId { get; set; }
    public string DubContentHash { get; set; } = "";
```

In `ToModel()` add `DubMediaId = MediaId.ParseNullable(DubMediaId), DubContentHash = new HashString(DubContentHash),`; in `UpdateFrom(model)` add `DubMediaId = model.DubMediaId?.Value; DubContentHash = model.DubContentHash.Value ?? "";` (check `HashString`'s string accessor name in `src/dotnet/Core/Hashing/HashString.cs` — use whatever `SourceContentHash` uses in the same method).

Generate the migration (the project must be built first):

```bash
dotnet build src/dotnet/Chat.Service.Migration/Chat.Service.Migration.csproj
./ef-migrations.cmd Chat.Service add Add_Translation_Dub
```

Verify the generated `Up` adds `dub_media_id` (text, nullable) and `dub_content_hash` (text, not null, default `""`) to `translations`, and that the snapshot changed only for `DbTranslation`.

- [ ] **Step 6: Write the failing integration test — a content change drops the dub**

`tests/Chat.IntegrationTests/TranslationDubTest.cs` (uses the existing `DubbingTranslationCollection` fixture from `DubbingTranslationFlowTest.cs`, which enables translation and the fake translator):

```csharp
using ActualChat.Media;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(DubbingTranslationCollection))]
public class TranslationDubTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);

    [Fact(Timeout = 60_000)]
    public async Task ANewContentClearsTheDubAndDeletesItsMedia()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var commander = services.Commander();
        var translations = services.GetRequiredService<ITranslationsBackend>();
        var mediaBackend = services.GetRequiredService<IMediaBackend>();
        var entry = await Tester.CreateTextEntry(chatId, "Привет");
        var id = TranslationId.New(entry.Id, Languages.English);
        var translation = await translations.Get(id, translateIfMissing: true, CancellationToken.None);
        translation.Should().NotBeNull();
        var mediaId = MediaId.New(chatId.Value);
        await commander.Call(new MediaBackend_Change(mediaId, null, Change.Create(new MediaFull(mediaId) {
            BlobId = "audio-record/test~en-US.webm",
            ContentType = "audio/webm",
        })));
        translation = await commander.Call(new TranslationsBackend_Change(id, translation!.Version,
            Change.Update(new TranslationDiff {
                DubMediaId = mediaId,
                DubContentHash = ChatEntryHashExt.GetContentHashString(translation.Content),
            })));
        translation!.HasValidDub.Should().BeTrue();

        // act
        translation = await commander.Call(new TranslationsBackend_Change(id, translation.Version,
            Change.Update(new TranslationDiff {
                Content = "Hello there",
                SourceContentHash = ChatEntryHashExt.GetContentHashString("Привет там"),
            })));

        // assert
        translation!.DubMediaId.Should().BeNull("a dub of the old content must not be served");
        translation.DubContentHash.IsNone.Should().BeTrue();
        var media = await mediaBackend.Get(mediaId, CancellationToken.None);
        media.Should().BeNull("the orphaned media is deleted with the change");
    }
}
```

- [ ] **Step 7: Run it to see it fail**

Run: `dotnet build tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj && dotnet test --no-build tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~TranslationDubTest"`
Expected: FAIL — `DubMediaId` still set after the content change.

- [ ] **Step 8: Clear the dub on content change and delete the media**

In `TranslationsBackend.OnChange`, inside the local `ApplyDiff`, after `DiffEngine.Patch` and before validation:

```csharp
            // A dub is audio of one specific Content; a new Content orphans it
            if (diff?.Content is { } content && !OrdinalEquals(content, originalTranslation.Content))
                newTranslation = newTranslation with { DubMediaId = null, DubContentHash = HashString.None };
```

Track the orphan: before `ApplyDiff` in the update branch, remember `var previousDubMediaId = dbTranslation.ToModel().DubMediaId;` (reuse the model you already build there); after `SaveChangesAsync`, when `previousDubMediaId is { } orphan && translation.DubMediaId != orphan`:

```csharp
            await Commander.Call(new MediaBackend_Change(orphan, null, Change.Remove<MediaFull>()), true, cancellationToken)
                .ConfigureAwait(false);
```

(`Commander` is available on `DbServiceBase`; the nested command runs inside the same operation scope, as `TranslationsBackend.OnTranslate` already does for `TranslationsBackend_Change`.) `OrdinalEquals` is the project's `string.Equals(..., StringComparison.Ordinal)` helper — grep `OrdinalEquals(` in `src/dotnet/Core` to confirm the name; use `string.Equals(content, originalTranslation.Content, StringComparison.Ordinal)` if it doesn't exist.

- [ ] **Step 9: Run both tests**

Run the unit and integration filters from steps 4 and 7.
Expected: all pass. Also run `dotnet test --no-build tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~DubbingTranslationFlowTest"` — still 4 passed (the clear-on-content-change must not fire for streaming increments: `diff.Content` there is the growing text, and no dub is set, so nothing is deleted; but verify no exception).

- [ ] **Step 10: Commit**

```bash
git add src/dotnet/Api/Chat/Translation.cs src/dotnet/Chat.Service/Db/DbTranslation.cs src/dotnet/Chat.Service/TranslationsBackend.cs src/dotnet/Chat.Service.Migration/Migrations tests/Chat.UnitTests/TranslationDubTest.cs tests/Chat.IntegrationTests/TranslationDubTest.cs
git commit -m "feat(dubbing): let a translation own its synthesized dub media"
```

---

### Task 2: One-shot synthesis

**Files:**
- Modify: `src/dotnet/Transcription.Contracts/ISpeechSynthesizer.cs`
- Modify: `src/dotnet/Transcription.Service/Synthesis/OpusFramePump.cs`
- Create: `src/dotnet/Transcription.Service/Synthesis/SpeechSynthesizerExt.cs`
- Modify: `src/dotnet/Transcription.Service/Transcribers/SonioxTtsClient.cs`, `src/dotnet/Transcription.Service/ServiceCollectionExt.cs`
- Modify: `src/dotnet/Transcription.Service/Synthesis/SonioxSpeechSynthesizer.cs`, `.../FakeSpeechSynthesizer.cs`, `tests/Testing.Host/RecordingSpeechSynthesizer.cs`
- Test: `tests/Transcription.UnitTests/OpusFramePumpTest.cs` (extend if it exists, else create), `tests/Transcription.UnitTests/FakeSpeechSynthesizerTest.cs`

**Interfaces:**
- Produces: `Task<AudioSource> ISpeechSynthesizer.Synthesize(string text, SpeechSynthesisOptions options, CancellationToken ct = default)`; `OpusFramePump(MomentClock clock, bool isPaced = true)`; `SonioxTtsClient.Generate(string language, string voice, string text, ChannelWriter<byte[]> pcm, CancellationToken ct)`; `SonioxTtsClient.HttpClientName`; `RecordingSpeechSynthesizer` records one-shot texts under the stream id `"<language>:<text hash>"` — tests use `WhenSpoken(...)` on it exactly as for live.

- [ ] **Step 1: Write the failing pump test**

`tests/Transcription.UnitTests/OpusFramePumpTest.cs` (add to the existing file if present):

```csharp
    [Fact]
    public async Task AnUnpacedPumpEncodesASecondOfPcmWithoutWaitingASecond()
    {
        var pcm = Channel.CreateUnbounded<byte[]>();
        var output = Channel.CreateUnbounded<AudioFrame>();
        using var pump = new OpusFramePump(MomentClockSet.Default.CpuClock, isPaced: false);
        pcm.Writer.TryWrite(new byte[OpusFramePump.FrameByteLength * 50]);
        pcm.Writer.Complete();

        var startedAt = CpuTimestamp.Now;
        await pump.Run(pcm.Reader, output.Writer, CancellationToken.None);
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        frames.Should().HaveCount(50);
        frames[^1].Offset.Should().Be(TimeSpan.FromMilliseconds(20 * 49));
        startedAt.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500), "no pacing delay");
    }
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet build tests/Transcription.UnitTests/Transcription.UnitTests.csproj 2>&1 | grep -E " error |Build succeeded"`
Expected: `error CS1739` — no `isPaced` parameter.

- [ ] **Step 3: Make pacing optional**

`OpusFramePump`: constructor `public OpusFramePump(MomentClock clock, bool isPaced = true)`, store `private bool IsPaced { get; }`, and in `Run` guard the delay:

```csharp
                if (IsPaced) {
                    var delay = startedAt + Constants.Audio.OpusFrameDuration * frameIndex - Clock.Now;
                    if (delay > TimeSpan.Zero)
                        await Clock.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
```

Update the class summary's first line to "…emitted at wall-clock pace by default;".

- [ ] **Step 4: Run the pump test**

Expected: PASS.

- [ ] **Step 5: Write the failing one-shot synthesizer test**

`tests/Transcription.UnitTests/FakeSpeechSynthesizerTest.cs`:

```csharp
using ActualChat.Transcription;

namespace ActualChat.Transcription.UnitTests;

public class FakeSpeechSynthesizerTest
{
    [Fact]
    public async Task OneShotSynthesisReturnsAWholeAudioSource()
    {
        var services = new ServiceCollection().AddSingleton(MomentClockSet.Default).BuildServiceProvider();
        var synthesizer = new FakeSpeechSynthesizer(services);

        var audio = await synthesizer.Synthesize("Hello there, how are you?", new SpeechSynthesisOptions(Languages.English));
        var frames = await audio.GetFrames(CancellationToken.None).ToListAsync();

        frames.Should().HaveCount(6, "one 20ms frame per four characters");
        await audio.WhenDurationAvailable;
        audio.Duration.Should().Be(TimeSpan.FromMilliseconds(120));
    }
}
```

(If `FakeSpeechSynthesizer` needs more than `MomentClockSet` from `services`, mirror whatever the live `SonioxSpeechSynthesizer` tests in `tests/Transcription.UnitTests` build — grep `new FakeSpeechSynthesizer(` there.)

- [ ] **Step 6: Run it to see it fail**

Expected: `error CS1501` — no `Synthesize(string, SpeechSynthesisOptions)` overload.

- [ ] **Step 7: Add the contract and the shared plumbing**

`ISpeechSynthesizer` — add:

```csharp
    Task<AudioSource> Synthesize(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default);
```

Update the interface summary: "…Speaks a stream of text chunks … or one text as a whole, unpaced, as an <see cref="AudioSource"/>."

`src/dotnet/Transcription.Service/Synthesis/SpeechSynthesizerExt.cs`:

```csharp
using ActualChat.Audio;

namespace ActualChat.Transcription;

public static class SpeechSynthesizerExt
{
    // One-shot synthesis shares this tail: a PCM producer feeds an unpaced pump, and the frames
    // it emits become an AudioSource whose duration is known once the producer is done
    public static AudioSource ToAudioSource(
        Func<ChannelWriter<byte[]>, CancellationToken, Task> producePcm,
        MomentClockSet clocks,
        ILogger log,
        CancellationToken cancellationToken)
    {
        var pcm = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var output = Channel.CreateUnbounded<AudioFrame>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        _ = BackgroundTask.Run(async () => {
            using var pump = new OpusFramePump(clocks.CpuClock, isPaced: false);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await TranscriberHelper.WhenPushAndRead(
                    producePcm.Invoke(pcm.Writer, cts.Token),
                    pump.Run(pcm.Reader, output.Writer, cts.Token),
                    cts)
                .ConfigureAwait(false);
        }, log, "One-shot synthesis failed", cancellationToken);
        return new AudioSource(
            clocks.SystemClock.Now,
            AudioSource.DefaultFormat,
            output.Reader.ReadAllAsync(cancellationToken),
            TimeSpan.Zero,
            log,
            cancellationToken);
    }
}
```

Check `AudioSource`'s frame stream contract in `src/dotnet/Api/Media/MediaSource.cs`: it sets `Duration` from the last frame's `Offset + Duration` when the stream ends — pump frames carry `Offset` only, so set `Duration = Constants.Audio.OpusFrameDuration` in `OpusFramePump.Encode` if `MediaSource` relies on it (read `MediaSource.cs` first; `AudioFrame.Duration` is used in `ProcessAudio` for the same purpose).

- [ ] **Step 8: Implement the one-shot overload on the three synthesizers**

`FakeSpeechSynthesizer`:

```csharp
    public Task<AudioSource> Synthesize(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default)
        => Task.FromResult(SpeechSynthesizerExt.ToAudioSource(
            (pcm, ct) => PushOne(text, pcm, ct), Clocks, Log, cancellationToken));

    private static async Task PushOne(string text, ChannelWriter<byte[]> pcm, CancellationToken cancellationToken)
    {
        var frameCount = Math.Max(1, text.Length / 4);
        await pcm.WriteAsync(new byte[OpusFramePump.FrameByteLength * frameCount], cancellationToken).ConfigureAwait(false);
        pcm.TryComplete();
    }
```

(add `private ILogger Log { get; } = services.LogFor<FakeSpeechSynthesizer>();`).

`SonioxSpeechSynthesizer`:

```csharp
    public Task<AudioSource> Synthesize(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default)
    {
        var client = new SonioxTtsClient(Services);
        var voice = options.VoiceId ?? Settings.SonioxTtsVoice;
        return Task.FromResult(SpeechSynthesizerExt.ToAudioSource(
            (pcm, ct) => client.Generate(options.Language.ToSoniox(), voice, text, pcm, ct),
            Clocks, Log, cancellationToken));
    }
```

`RecordingSpeechSynthesizer` (tests/Testing.Host):

```csharp
    public Task<AudioSource> Synthesize(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default)
    {
        Record(OneShotStreamId(options.Language, text), text);
        return Inner.Synthesize(text, options, cancellationToken);
    }

    public static string OneShotStreamId(Language language, string text)
        => $"{language.Value}:{text.GetHashCode()}";
```

Refactor the existing `Record` loop so the `lock`ed add + `_whenChangedSource` swap is a private `Record(string streamId, string chunk)` method both paths call.

- [ ] **Step 9: Implement `SonioxTtsClient.Generate` (REST)**

In `SonioxTtsClient`:

```csharp
    public const string HttpClientName = nameof(SonioxTtsClient);
    private const string RestUrl = "https://tts-rt.soniox.com/tts";
    private const int MaxTextLength = 5000;

    private IHttpClientFactory HttpClientFactory { get; } = services.HttpClientFactory();

    public async Task Generate(
        string language,
        string voice,
        string text,
        ChannelWriter<byte[]> pcm,
        CancellationToken cancellationToken)
    {
        var apiKey = CoreServerSettings.SonioxKey;
        if (apiKey.IsNullOrEmpty())
            throw StandardError.Configuration("CoreSettings:SonioxKey is not set.");

        Exception? error = null;
        try {
            using var httpClient = HttpClientFactory.CreateClient(HttpClientName);
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            foreach (var part in SplitText(text, MaxTextLength)) {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TtsChunkTimeout);
                using var response = await httpClient.PostAsJsonAsync(RestUrl, new {
                    model = Model,
                    language,
                    voice,
                    audio_format = PcmFormat,
                    sample_rate = SampleRate,
                    text = part,
                }, JsonOptions, cts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) {
                    var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    throw StandardError.External($"Soniox TTS returned {(int)response.StatusCode}: {body}");
                }
                var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
                await pcm.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            error = e;
            if (e is not OperationCanceledException)
                Log.LogError(e, "Soniox TTS generation failed");
            throw;
        }
        finally {
            pcm.TryComplete(error);
        }
    }

    // Soniox caps a request at 5000 characters; longer text is cut at sentence ends
    internal static IEnumerable<string> SplitText(string text, int maxLength)
    {
        var start = 0;
        while (text.Length - start > maxLength) {
            var end = text.LastIndexOfAny(['.', '!', '?', '\n'], start + maxLength - 1, maxLength);
            if (end <= start)
                end = text.LastIndexOf(' ', start + maxLength - 1, maxLength);
            if (end <= start)
                end = start + maxLength - 1;
            yield return text[start..(end + 1)];
            start = end + 1;
        }
        if (start < text.Length)
            yield return text[start..];
    }
```

Add `using System.Net.Http.Headers; using System.Net.Http.Json;`. Register the client in `ServiceCollectionExt.AddSoniox`: `services.AddHttpClient(SonioxTtsClient.HttpClientName);`.

Add a `SplitText` unit test to `tests/Transcription.UnitTests/SonioxTtsClientTest.cs` (new file): a 12,000-char text of `"Sentence one. "` repeats splits into ≥ 3 parts, each ≤ 5000 chars, each ending with `.` or a space, and `string.Concat(parts) == text`.

- [ ] **Step 10: Run the Transcription unit tests**

Run: `dotnet build tests/Transcription.UnitTests/Transcription.UnitTests.csproj && dotnet test --no-build tests/Transcription.UnitTests/Transcription.UnitTests.csproj --filter "FullyQualifiedName~OpusFramePumpTest|FullyQualifiedName~FakeSpeechSynthesizerTest|FullyQualifiedName~SonioxTtsClientTest"`
Expected: all pass. Also build `tests/Testing.Host/Testing.Host.csproj` to compile the recorder.

- [ ] **Step 11: Commit**

```bash
git add src/dotnet/Transcription.Contracts/ISpeechSynthesizer.cs src/dotnet/Transcription.Service tests/Testing.Host/RecordingSpeechSynthesizer.cs tests/Transcription.UnitTests
git commit -m "feat(dubbing): one-shot speech synthesis for a whole text"
```

---

### Task 3: `ReplayDubs` — get or create the dub media

**Files:**
- Modify: `src/dotnet/Streaming.Service/Services/AudioSegmentSaver.cs`
- Create: `src/dotnet/Streaming.Service/Services/ReplayDubs.cs`
- Modify: `src/dotnet/Streaming.Service/Module/StreamingServiceModule.cs:41` (register), `src/dotnet/Api/Constants.Audio.cs:146-156`
- Test: `tests/Chat.IntegrationTests/ReplayDubsTest.cs`

**Interfaces:**
- Consumes: `Translation.HasValidDub`, `TranslationDiff.DubMediaId/DubContentHash` (Task 1); `ISpeechSynthesizer.Synthesize(text, options, ct)` (Task 2).
- Produces: `Task<Media?> ReplayDubs.GetOrCreate(ChatEntry entry, Language language, CancellationToken ct)`; `Constants.Audio.ReplayDubTimeout` (20 s), `Constants.Audio.ReplayDubLookahead` (2); `AudioSegmentSaver.SaveAndCreateMedia(AudioSource audio, string blobId, ChatId chatId, CancellationToken ct)`.

- [ ] **Step 1: Write the failing integration test**

`tests/Chat.IntegrationTests/ReplayDubsTest.cs`:

```csharp
using ActualChat.Media;
using ActualChat.Streaming.Services;
using ActualChat.Testing.Host;
using ActualChat.Transcription;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(DubbingTranslationCollection))]
public class ReplayDubsTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);

    [Fact(Timeout = 90_000)]
    public async Task TheFirstRequestCreatesTheDubAndTheSecondReusesIt()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var dubs = services.GetRequiredService<ReplayDubs>();
        var translations = services.GetRequiredService<ITranslationsBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.Russian);
        var ct = CancellationToken.None;

        // act
        var first = await dubs.GetOrCreate(entry, Languages.English, ct);
        var second = await dubs.GetOrCreate(entry, Languages.English, ct);

        // assert
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        second!.Id.Should().Be(first!.Id, "the dub is stored on the translation and reused");
        var translation = await translations.Get(TranslationId.New(entry.Id, Languages.English), false, ct);
        translation!.HasValidDub.Should().BeTrue();
        translation.DubMediaId.Should().Be(first.Id);
        var spoken = recorder.GetChunks(RecordingSpeechSynthesizer.OneShotStreamId(Languages.English, translation.Content));
        spoken.Should().Equal([translation.Content], "synthesized once, from the stored translation");
    }

    [Fact(Timeout = 90_000)]
    public async Task AnEntryAlreadyInTheListenersLanguageIsNotDubbed()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var dubs = Tester.AppServices.GetRequiredService<ReplayDubs>();
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.English);

        var dub = await dubs.GetOrCreate(entry, Languages.English, CancellationToken.None);

        dub.Should().BeNull();
    }

    [Fact(Timeout = 90_000)]
    public async Task ARetranslationRegeneratesTheDub()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var dubs = services.GetRequiredService<ReplayDubs>();
        var commander = services.Commander();
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.Russian);
        var ct = CancellationToken.None;
        var first = await dubs.GetOrCreate(entry, Languages.English, ct);
        var id = TranslationId.New(entry.Id, Languages.English);
        var translation = await services.GetRequiredService<ITranslationsBackend>().Get(id, false, ct);
        await commander.Call(new TranslationsBackend_Change(id, translation!.Version, Change.Update(new TranslationDiff {
            Content = translation.Content + " Again.",
            SourceContentHash = translation.SourceContentHash,
        })));

        var second = await dubs.GetOrCreate(entry, Languages.English, ct);

        second.Should().NotBeNull();
        second!.Id.Should().NotBe(first!.Id, "the old dub spoke the old text");
    }
}
```

`RecordVoiceEntry` (tests/Testing.Host/AudioRecordingOperations.cs) runs the fake transcriber with the chat language, so the entry's `ChatEntryLanguage` is that language; the fake translator produces `"English: <text>"`.

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet build tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj 2>&1 | grep -E " error |Build succeeded"`
Expected: `ReplayDubs` does not exist.

- [ ] **Step 3: Constants and the saver overload**

`src/dotnet/Api/Constants.Audio.cs`, after `DubBacklogThreshold`:

```csharp
        // A replay dub that isn't ready within this is served as the original for that entry
        public static readonly TimeSpan ReplayDubTimeout = TimeSpan.FromSeconds(20);
        // Entries whose dubs are prepared while the current one streams
        public static readonly int ReplayDubLookahead = 2;
```

`AudioSegmentSaver` — add:

```csharp
    public async Task<MediaId> SaveAndCreateMedia(
        AudioSource audio,
        string blobId,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var converter = new WebMStreamConverter(Clocks, Log);
        var byteStream = converter.ToByteStream(audio, cancellationToken);
        await Blobs[BlobScope.AudioRecord].UploadByteStream(blobId, byteStream, cancellationToken).ConfigureAwait(false);
        await audio.WhenDurationAvailable.ConfigureAwait(false);

        var mediaId = MediaId.New(chatId.Value);
        var media = new MediaFull(mediaId) {
            BlobId = blobId,
            ContentType = "audio/webm",
            BeginsAt = default,
            EndsAt = default(Moment) + audio.Duration,
            ContentEndsAt = default(Moment) + audio.Duration,
        };
        await Commander.Call(new MediaBackend_Change(mediaId, null, Change.Create(media)), cancellationToken).ConfigureAwait(false);
        return mediaId;
    }
```

- [ ] **Step 4: Implement `ReplayDubs`**

`src/dotnet/Streaming.Service/Services/ReplayDubs.cs`:

```csharp
using ActualChat.Chat;
using ActualChat.Media;
using ActualChat.Transcription;

namespace ActualChat.Streaming.Services;

// A dub for replay is the stored translation of an entry spoken once and kept as media on that
// translation; it's made the first time a listener needs it and reused until the translation changes
public sealed class ReplayDubs(IServiceProvider services)
{
    private readonly ConcurrentDictionary<(ChatEntryId, Language), Task<Media?>> _inFlight = new();

    private IChatEntryLanguagesBackend EntryLanguages { get; } = services.GetRequiredService<IChatEntryLanguagesBackend>();
    private ITranslationsBackend Translations { get; } = services.GetRequiredService<ITranslationsBackend>();
    private IMediaBackend MediaBackend { get; } = services.GetRequiredService<IMediaBackend>();
    private ISpeechSynthesizer Synthesizer { get; } = services.GetRequiredService<ISpeechSynthesizer>();
    private AudioSegmentSaver Saver { get; } = services.GetRequiredService<AudioSegmentSaver>();
    private ICommander Commander { get; } = services.Commander();
    private ILogger Log { get; } = services.LogFor<ReplayDubs>();

    public Task<Media?> GetOrCreate(ChatEntry entry, Language language, CancellationToken cancellationToken)
    {
        var key = (entry.Id, language);
        // The first caller does the work; the others await the same task, and it's forgotten once done
        var task = _inFlight.GetOrAdd(key, _ => Run());
        return task.WaitAsync(cancellationToken);

        async Task<Media?> Run()
        {
            try {
                using var cts = new CancellationTokenSource(Constants.Audio.ReplayDubTimeout);
                return await GetOrCreateImpl(entry, language, cts.Token).ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogInformation(e, "GetOrCreate: no {Language} dub for #{EntryId}, serving the original",
                    language, entry.Id);
                return null;
            }
            finally {
                _inFlight.TryRemove(key, out _);
            }
        }
    }

    // Private methods

    private async Task<Media?> GetOrCreateImpl(ChatEntry entry, Language language, CancellationToken cancellationToken)
    {
        if (entry.Audio is not { } audio || audio.BlobId.IsNullOrEmpty() || entry.Content.IsNullOrWhiteSpace())
            return null;
        if (await IsSpokenIn(entry, language, cancellationToken).ConfigureAwait(false))
            return null;

        var id = TranslationId.New(entry.Id, language);
        var translation = await Translations.Get(id, translateIfMissing: true, cancellationToken).ConfigureAwait(false);
        if (translation == null || translation.IsStreaming || translation.MatchesOriginal(entry.Content))
            return null;
        if (translation.HasValidDub) {
            var existing = await MediaBackend.Get(translation.DubMediaId, cancellationToken).ConfigureAwait(false);
            if (existing != null)
                return existing;
            // The media is gone; fall through and make it again
        }

        var text = translation.Content;
        var synthesized = await Synthesizer.Synthesize(text, new SpeechSynthesisOptions(language), cancellationToken)
            .ConfigureAwait(false);
        var blobId = BlobPath.Format(BlobScope.AudioRecord, audio.StreamId.NullIfEmpty() ?? entry.Id.Value, $"{language.Value}.webm");
        var mediaId = await Saver.SaveAndCreateMedia(synthesized, blobId, entry.ChatId, cancellationToken).ConfigureAwait(false);
        var stamped = await Commander.Call(new TranslationsBackend_Change(id, translation.Version, Change.Update(new TranslationDiff {
            DubMediaId = mediaId,
            DubContentHash = ChatEntryHashExt.GetContentHashString(text),
        })), true, cancellationToken).ConfigureAwait(false);
        if (stamped?.DubMediaId != mediaId) {
            // A re-translation raced us: its content is newer than what we spoke
            await Commander.Call(new MediaBackend_Change(mediaId, null, Change.Remove<MediaFull>()), true, cancellationToken)
                .ConfigureAwait(false);
            return null;
        }
        return await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsSpokenIn(ChatEntry entry, Language language, CancellationToken cancellationToken)
    {
        var idTile = Constants.Chat.EntryIdTiles.GetTile(entry.LocalId);
        var tile = await EntryLanguages.GetTile(entry.ChatId, idTile.Range, cancellationToken).ConfigureAwait(false);
        var entryLanguage = tile.Entries.FirstOrDefault(x => x.Id == entry.Id);
        return entryLanguage != null && entryLanguage.Languages.Any(x => x.IsoCode == language.IsoCode);
    }
}
```

Notes for the implementer: `TranslationsBackend_Change` with a stale `ExpectedVersion` throws a version-mismatch error — catch that specific exception type (grep `RequireVersion` in `src/dotnet/Core` for what it throws) around the stamping call and treat it like the `stamped?.DubMediaId != mediaId` branch. `BlobPath.Format` is used by `AudioSegmentSaver.Save` — copy its argument shape. Register in `StreamingServiceModule` next to `AudioSegmentSaver`: `services.AddSingleton<ReplayDubs>();`.

- [ ] **Step 5: Run the test**

Run: `dotnet build tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj && dotnet test --no-build tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~ReplayDubsTest"`
Expected: 3 passed. The second test relies on `RecordVoiceEntry(chatId, Languages.English)` storing `[en]` as the entry's languages (the fake transcriber reports `options.Language`, and `AudioStreamingBackend.ProcessAudio.CreateLanguages` persists it). If the test host stores no languages for the entry, fix the test arrangement (make the entry language explicit through `ChatEntryLanguagesBackend_Change.Upsert`, as `CreateLanguages` does) — do not loosen the production rule.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Api/Constants.Audio.cs src/dotnet/Streaming.Service tests/Chat.IntegrationTests/ReplayDubsTest.cs
git commit -m "feat(dubbing): synthesize and store a replay dub per translation"
```

---

### Task 4: Replay muxer serves the dub

**Files:**
- Modify: `src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs:44-50`
- Modify: `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs:207-222`
- Modify: `src/dotnet/Streaming.Service/Services/ReplayStreamMuxer.cs`
- Test: `tests/Streaming.UnitTests/ReplayTimelineTest.cs`, `tests/Chat.IntegrationTests/ReplayDubbingTest.cs`

**Interfaces:**
- Consumes: `ReplayDubs.GetOrCreate(entry, language, ct)` (Task 3).
- Produces: `ILiveAudioStreams.GetReplayStream(Session, ChatId, Moment startAt, TimeSpan rewindOffset, double speed, Language? dubLanguage, CancellationToken)`; `ReplayStreamMuxer(services, session, chatId, startAt, rewindOffset, speed, Language? dubLanguage = null)`; `internal static class ReplayTimeline` with `PlaysAt(TimeSpan timelinePlaysAt, TimeSpan notBefore)` and `ScaleSkip(TimeSpan skipTo, TimeSpan entryDuration, TimeSpan dubDuration)`.

- [ ] **Step 1: Write the failing timeline unit test**

`tests/Streaming.UnitTests/ReplayTimelineTest.cs`:

```csharp
using ActualChat.Streaming.Services;

namespace ActualChat.Streaming.UnitTests;

public class ReplayTimelineTest
{
    [Fact]
    public void AnEntryNeverStartsBeforeThePreviousDubEnds()
    {
        ReplayTimeline.PlaysAt(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12)).Should().Be(TimeSpan.FromSeconds(12));
        ReplayTimeline.PlaysAt(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(8)).Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ASeekIntoADubbedEntryIsScaledToTheDubsLength()
    {
        ReplayTimeline.ScaleSkip(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(9))
            .Should().Be(TimeSpan.FromSeconds(4.5));
        ReplayTimeline.ScaleSkip(TimeSpan.FromSeconds(3), TimeSpan.Zero, TimeSpan.FromSeconds(9))
            .Should().Be(TimeSpan.Zero, "an entry without a known duration can't be scaled, so the dub starts over");
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet build tests/Streaming.UnitTests/Streaming.UnitTests.csproj 2>&1 | grep -E " error |Build succeeded"`
Expected: `ReplayTimeline` does not exist.

- [ ] **Step 3: Add `ReplayTimeline`**

At the bottom of `ReplayStreamMuxer.cs` (or its own file `ReplayTimeline.cs` in the same folder):

```csharp
internal static class ReplayTimeline
{
    public static TimeSpan PlaysAt(TimeSpan timelinePlaysAt, TimeSpan notBefore)
        => TimeSpanExt.Max(timelinePlaysAt, notBefore);

    public static TimeSpan ScaleSkip(TimeSpan skipTo, TimeSpan entryDuration, TimeSpan dubDuration)
        => entryDuration <= TimeSpan.Zero || skipTo <= TimeSpan.Zero
            ? TimeSpan.Zero
            : skipTo * (dubDuration / entryDuration);
```

(`TimeSpanExt.Max` — grep `static TimeSpan Max` in `src/dotnet/Core`; use `skipTo > notBefore ? … : …` inline if it isn't there. Make the Streaming.Service internals visible to the unit tests if they aren't yet: check `InternalsVisibleTo` in `Streaming.Service.csproj`; `ListeningStreamMuxer.MustDub` is `internal static` and already tested from `ListeningStreamMuxerRelayTest`, so it is.)

- [ ] **Step 4: Run the unit test**

Expected: PASS.

- [ ] **Step 5: Write the failing integration test**

`tests/Chat.IntegrationTests/ReplayDubbingTest.cs`:

```csharp
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.Transcription;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(DubbingTranslationCollection))]
public class ReplayDubbingTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);

    [Fact(Timeout = 90_000)]
    public async Task AReplayForAnEnglishListenerSpeaksTheRussianEntryInEnglish()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.Russian);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;

        // act
        var stream = await liveStreams.GetReplayStream(
            Tester.Session, chatId, entry.BeginsAt, TimeSpan.Zero, 1.0, Languages.English, ct);
        var items = await stream.ToListAsync(ct);

        // assert
        var start = items.OfType<MuxedAudioStreamStart>().Should().ContainSingle().Subject;
        start.StreamInfo.DubLanguage.Should().Be(Languages.English);
        start.StreamInfo.EntryId.Should().Be(entry.Id);
        items.OfType<MuxedAudioFrame>().Should().NotBeEmpty();
        var translation = await services.GetRequiredService<ITranslationsBackend>()
            .Get(TranslationId.New(entry.Id, Languages.English), false, ct);
        recorder.GetChunks(RecordingSpeechSynthesizer.OneShotStreamId(Languages.English, translation!.Content))
            .Should().Equal([translation.Content]);
    }

    [Fact(Timeout = 90_000)]
    public async Task AReplayWithoutADubLanguageIsUnchanged()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var liveStreams = Tester.AppServices.GetRequiredService<ILiveAudioStreams>();
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.Russian);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var stream = await liveStreams.GetReplayStream(
            Tester.Session, chatId, entry.BeginsAt, TimeSpan.Zero, 1.0, null, cts.Token);
        var items = await stream.ToListAsync(cts.Token);

        items.OfType<MuxedAudioStreamStart>().Should().ContainSingle().Which.StreamInfo.DubLanguage.Should().BeNull();
    }
}
```

`GetReplayStream` needs the listener to have English as a language for `IsDubLanguageAllowed` — read `LiveAudioStreams.IsDubLanguageAllowed` and set the same settings `RecordVoiceEntry` sets (`UserLanguageSettings.Primary`) to English for the *listening* user before the act; simplest: `RecordVoiceEntry` sets Alice's primary language to Russian, so before `GetReplayStream` set it back with `services.UserSettingsUI(Tester.Session).UserLanguageSettings().Set(new UserLanguageSettings { Primary = Languages.English }, ct)` and also enable translation for the chat the same way `DubbingTranslationFlowTest` gets it (it relies on `ChatSettings.IsTranslationEnabled`; check whether `IsDubLanguageAllowed` also needs `ChatUserSettings.Language` — set it if so).

- [ ] **Step 6: Run it to see it fail**

Expected: `error CS1501` — no 7-arg `GetReplayStream`.

- [ ] **Step 7: Add the overload**

`ILiveAudioStreams` — after the existing `GetReplayStream`:

```csharp
    Task<RpcStream<MuxedAudioStreamItem>> GetReplayStream(
        Session session,
        ChatId chatId,
        Moment startAt,
        TimeSpan rewindOffset,
        double speed,
        Language? dubLanguage,
        CancellationToken cancellationToken);
```

`LiveAudioStreams` — the existing 6-arg method delegates: `=> GetReplayStream(session, chatId, startAt, rewindOffset, speed, null, cancellationToken);` and the new one does what the 5-arg listening overload does with `dubLanguage` (same `IsDubLanguageAllowed` + `Languages.GetCanonical` + warning, log line `"GetReplayStream: chat '{ChatId}', startAt={StartAt}, dub={DubLanguage}"`), then `new ReplayStreamMuxer(Services, session, chatId, startAt, rewindOffset, speed, dubLanguage)`.

Check `ILiveAudioStreams` has the same RPC-timeout attribute on `GetReplayStream` as on the listening overloads (the comment at line 51 of the contract) and copy it to the new overload.

- [ ] **Step 8: Dub in the muxer**

`ReplayStreamMuxer`:
- ctor gains `Language? dubLanguage = null`; `private Language? DubLanguage { get; }`; `private ReplayDubs Dubs => field ??= Services.GetRequiredService<ReplayDubs>();`.
- In `OnRun`, keep a `var notBefore = TimeSpan.Zero;` and a lookahead map `var dubTasks = new Dictionary<ChatEntryId, Task<Media?>>();`. Replace the entry loop body's tail with:

```csharp
                var skipTo = (resolvedStartAt.Value - entry.BeginsAt).Positive();
                var timelinePlaysAt = (entry.BeginsAt - resolvedStartAt.Value - gapAdjustment).Positive() / Speed;
                var dub = DubLanguage == null ? null : await GetDub(entry).ConfigureAwait(false);
                var playsAt = ReplayTimeline.PlaysAt(timelinePlaysAt, notBefore);
                var playedDuration = dub != null
                    ? dub.EndsAt - dub.BeginsAt
                    : entryEndsAt - entry.BeginsAt - skipTo;
                notBefore = playsAt + playedDuration.Positive() / Speed;

                var streamIndex = Interlocked.Increment(ref _nextStreamIndex);
                var streamTask = ProcessEntry(entry, dub, streamIndex, skipTo, playsAt, cancellationToken);
```

with lookahead: iterate `entries` through an explicit `IAsyncEnumerator<ChatEntry>`, keep a `Queue<ChatEntry>` of the next `Constants.Audio.ReplayDubLookahead` entries (filled before each iteration), and when an entry is queued and `DubLanguage != null`, start `Dubs.GetOrCreate(next, DubLanguage, cancellationToken)` and store the task in `dubTasks`. `GetDub(entry)` returns `dubTasks.Remove(entry.Id, out var t) ? await t : await Dubs.GetOrCreate(entry, DubLanguage!, cancellationToken)`. `Media.BeginsAt/EndsAt` are metadata-backed on `Media` (see `src/dotnet/Api/Media/Media.cs:65-77`), and Task 3 stores the dub's duration there.

- `ProcessEntry(ChatEntry entry, Media? dub, int streamIndex, TimeSpan skipTo, TimeSpan playsAt, ct)`: when `dub != null`, `blobId = dub.BlobId`, `skipTo = ReplayTimeline.ScaleSkip(skipTo, entryEndsAt - entry.BeginsAt, dub.EndsAt - dub.BeginsAt)`, and `streamInfo = streamInfo with { DubLanguage = DubLanguage }` (the `StreamId` stays the entry's; the client keys tracks by it). Everything else unchanged; a download failure of the dub blob is caught in the existing `catch` — before it, add a targeted fallback: if `dub != null` and `AudioDownloader.Download` throws, log Information and retry the same call with the entry's own `blobId` and the unscaled `skipTo`.

- [ ] **Step 9: Run the integration tests**

Run: `dotnet build tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj && dotnet test --no-build tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~ReplayDubbingTest|FullyQualifiedName~ReplayDubsTest"`
Expected: all pass. Also run the existing replay tests: `--filter "FullyQualifiedName~Replay"` in `tests/Streaming.IntegrationTests` and `tests/Chat.IntegrationTests` — unchanged results.

- [ ] **Step 10: Commit**

```bash
git add src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs src/dotnet/Streaming.Service/Services tests/Streaming.UnitTests/ReplayTimelineTest.cs tests/Chat.IntegrationTests/ReplayDubbingTest.cs
git commit -m "feat(dubbing): serve the dub in replay, stretching the timeline to fit"
```

---

### Task 5: Client asks for the dub

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/Audio/ReplayStreamProcessor.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/Playback/ChatReplayPlayer.cs:64-68`
- Test: `tests/Chat.UI.Blazor.UnitTests/ReplayStreamProcessorTest.cs`

**Interfaces:**
- Consumes: 7-arg `ILiveAudioStreams.GetReplayStream` (Task 4).
- Produces: `ReplayStreamProcessor.DubLanguageProvider` (`Func<CancellationToken, Task<Language?>>?`, init).

- [ ] **Step 1: Write the failing test**

Model it on `tests/Chat.UI.Blazor.UnitTests/ListeningStreamProcessorTest.cs` (strict `Mock<ILiveAudioStreams>`; read it first and reuse its service-provider setup verbatim):

```csharp
    [Fact]
    public async Task ADubLanguageGoesToTheDubbingOverload()
    {
        var liveStreams = new Mock<ILiveAudioStreams>(MockBehavior.Strict);
        liveStreams
            .Setup(x => x.GetReplayStream(It.IsAny<Session>(), It.IsAny<ChatId>(), It.IsAny<Moment>(), It.IsAny<TimeSpan>(), 1.0, Languages.English, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyStream());
        var processor = new ReplayStreamProcessor(Services(liveStreams.Object), Session, ChatId, Moment.EpochStart, TimeSpan.Zero) {
            DubLanguageProvider = _ => Task.FromResult<Language?>(Languages.English),
        };

        await processor.Run();

        liveStreams.VerifyAll();
    }

    [Fact]
    public async Task NoDubLanguageKeepsTheOldOverload()
    {
        var liveStreams = new Mock<ILiveAudioStreams>(MockBehavior.Strict);
        liveStreams
            .Setup(x => x.GetReplayStream(It.IsAny<Session>(), It.IsAny<ChatId>(), It.IsAny<Moment>(), It.IsAny<TimeSpan>(), 1.0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyStream());
        var processor = new ReplayStreamProcessor(Services(liveStreams.Object), Session, ChatId, Moment.EpochStart, TimeSpan.Zero) {
            DubLanguageProvider = _ => Task.FromResult<Language?>(null),
        };

        await processor.Run();

        liveStreams.VerifyAll();
    }
```

(`EmptyStream()` = `StandardRpcStream.NewAudioDelivery(AsyncEnumerable.Empty<MuxedAudioStreamItem>(), allowReconnect: false)` or whatever `ListeningStreamProcessorTest` uses.)

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet build tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj 2>&1 | grep -E " error |Build succeeded"`
Expected: no `DubLanguageProvider` on `ReplayStreamProcessor`.

- [ ] **Step 3: Implement**

`ReplayStreamProcessor`: add `public Func<CancellationToken, Task<Language?>>? DubLanguageProvider { get; init; }` and in `OnRun`:

```csharp
            var dubLanguage = DubLanguageProvider == null
                ? null
                : await DubLanguageProvider.Invoke(cancellationToken).ConfigureAwait(false);
            Log.LogInformation("-> LiveStreams.GetReplayStream({ChatId}, {StartAt}, {RewindOffset}, speed={Speed}, dub={DubLanguage})",
                ChatId, StartAt, RewindOffset, Speed, dubLanguage);
            var stream = dubLanguage == null
                ? await liveStreams.GetReplayStream(Session, ChatId, StartAt, RewindOffset, Speed, cancellationToken).ConfigureAwait(false)
                : await liveStreams.GetReplayStream(Session, ChatId, StartAt, RewindOffset, Speed, dubLanguage, cancellationToken).ConfigureAwait(false);
```

`ChatReplayPlayer.Play`: `new ReplayStreamProcessor(...) { DubLanguageProvider = ct => Hub.TranslationUI.GetDubLanguage(ChatId, ct) }`.

- [ ] **Step 4: Run the tests**

Run: `dotnet test --no-build tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~StreamProcessorTest"` (after build).
Expected: all pass, including the existing `ListeningStreamProcessorTest`.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/Audio/ReplayStreamProcessor.cs src/dotnet/UI.Blazor.App/Services/Playback/ChatReplayPlayer.cs tests/Chat.UI.Blazor.UnitTests/ReplayStreamProcessorTest.cs
git commit -m "feat(dubbing): replay asks for the listener's dub language"
```

---

### Task 6: Docs

**Files:**
- Modify: `docs/live-audio/12-dubbing.md`
- Delete: `docs/superpowers/specs/2026-09-14-replay-dubbing-design.md`, `docs/superpowers/plans/2026-09-14-replay-dubbing.md` (once the doc carries the design — plans and specs are working documents)

- [ ] **Step 1: Write the replay section**

Add a `## Replay` section to `docs/live-audio/12-dubbing.md`, written from the code as it now is: the data model (`Translation.DubMediaId/DubContentHash`, `HasValidDub`, cleared on content change with the media deleted), `ReplayDubs.GetOrCreate` (skip rule, `translateIfMissing`, one-shot synthesis via `SonioxTtsClient.Generate` + unpaced `OpusFramePump` + `AudioSegmentSaver.SaveAndCreateMedia`, in-flight dedupe, `ReplayDubTimeout`), `ReplayStreamMuxer` (7-arg `GetReplayStream`, blob swap, `ReplayTimeline.PlaysAt` stretch, `ScaleSkip`, lookahead of `ReplayDubLookahead`, fallback to the original), the client (`ReplayStreamProcessor.DubLanguageProvider`, read once at replay start — a toggle mid-replay applies on the next start), and the "Out of scope / follow-ups" list (voice cloning: Soniox caps custom voices at 20/org; eager generation; re-subscribe on toggle during replay). Update the phase list at the top of the doc: replay is no longer phase 4.

- [ ] **Step 2: Remove the working documents and commit**

```bash
git rm docs/superpowers/specs/2026-09-14-replay-dubbing-design.md docs/superpowers/plans/2026-09-14-replay-dubbing.md
git add docs/live-audio/12-dubbing.md
git commit -m "docs(dubbing): describe replay dubbing"
```

- [ ] **Step 3: Full test pass for the touched projects**

Run (each after its build): `Transcription.UnitTests`, `Streaming.UnitTests`, `Chat.UnitTests`, `Chat.UI.Blazor.UnitTests` in full; `Chat.IntegrationTests --filter "FullyQualifiedName~Dub|FullyQualifiedName~Replay|FullyQualifiedName~Translation"`; `Streaming.IntegrationTests --filter "FullyQualifiedName~Dub|FullyQualifiedName~Replay"`. Report exact counts.
