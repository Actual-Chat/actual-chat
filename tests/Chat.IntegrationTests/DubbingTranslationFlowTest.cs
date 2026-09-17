using System.Numerics;
using ActualChat.Chat.Module;
using ActualChat.Module;
using ActualChat.Streaming;
using ActualChat.Streaming.Services;
using ActualChat.Testing.Audio;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using ActualChat.Transcription.Module;
using ActualLab.Rpc;

namespace ActualChat.Chat.IntegrationTests;

// Drives the dub off the real TranslationsBackend stream: the source transcript is pushed with
// Soniox-like diffs (unstable growth, one stable diff at the end) and the text entry the
// translation is keyed by is created after the transcript is published, as ProcessAudio does.

[Collection(nameof(DubbingTranslationCollection))]
public class DubbingTranslationFlowTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    private const string SourceText = "Привет, как у тебя сегодня дела?";
    private static readonly string[] SourceSteps = ["Привет, как", "Привет, как у тебя", SourceText];

    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);

    [Fact(Timeout = 90_000)]
    public Task DubShouldSpeakTheStableTranslation()
        => AssertDubSpeaksTheTranslation(TimeSpan.FromMilliseconds(100), mustRequestBeforeEntry: false);

    [Fact(Timeout = 90_000)]
    public Task DubShouldWaitForTheEntryTheTranslationIsKeyedBy()
        => AssertDubSpeaksTheTranslation(TimeSpan.FromMilliseconds(300), mustRequestBeforeEntry: true);

    [Fact(Timeout = 90_000)]
    public Task LateListenerShouldNotHearTheBacklog()
        => AssertDubSpeaksTheTranslation(TimeSpan.FromMilliseconds(100), mustRequestBeforeEntry: false,
            backlogSeconds: (float)Constants.Audio.DubBacklogThreshold.TotalSeconds + 1);

    [Fact(Timeout = 90_000)]
    public async Task DubShouldSpeakEachStablePhraseAsItArrives()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.English);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;
        const string firstPhrase = "Привет, как у тебя дела?";
        const string secondPhrase = firstPhrase + " Хорошо, спасибо.";
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var last = Transcript.Empty;
        Push(Unstable("Привет, как"));
        await backend.WhenTranscriptPublished(sourceId, ct);
        await Tester.CreateStreamingEntry(chatId, Languages.Russian, streamId: sourceId.Value, cancellationToken: ct);

        // act - the first phrase turns stable while the speaker goes on talking
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        Push(Stable(firstPhrase));
        var firstChunks = await recorder.WhenSpoken(dubId.Value, 1, ct);
        Push(Unstable(secondPhrase[..^5]));
        Push(Stable(secondPhrase));
        source.Writer.Complete();
        await pushSourceTask.SilentAwait(false);
        var chunks = await recorder.WhenSpoken(dubId.Value, 2, ct);

        // assert
        stream.Should().NotBeNull();
        firstChunks.Should().Equal([FakeTranslator.Translated(firstPhrase, Languages.English)],
            "a phrase that is final is spoken before the utterance ends");
        chunks.Should().HaveCount(2);
        chunks[1].Should().Contain("Хорошо, спасибо.").And.NotContain(firstPhrase,
            "the second chunk is only the increment, never a re-read of the first phrase");
        var frameCount = await stream!.CountAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(10), ct);
        frameCount.Should().BeGreaterThan(1, "the dub ends on its own once the whole source is translated");
        return;

        void Push(Transcript transcript) {
            source.Writer.TryWrite(transcript - last);
            last = transcript;
        }
    }

    [Fact(Timeout = 90_000)]
    public async Task DubShouldSpeakAStableFragmentWithoutAClauseEndWhenTheSourceEnds()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.English);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;
        const string fragment = "Привет, как у тебя";
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var last = Transcript.Empty;
        Push(Unstable(SourceSteps[0]));
        await backend.WhenTranscriptPublished(sourceId, ct);
        await Tester.CreateStreamingEntry(chatId, Languages.Russian, streamId: sourceId.Value, cancellationToken: ct);

        // act - the stable text stops mid-clause, and the speaker says nothing more
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        Push(Stable(fragment));
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        var heldChunks = recorder.GetChunks(dubId.Value);
        source.Writer.Complete();
        await pushSourceTask.SilentAwait(false);
        var chunks = await recorder.WhenSpoken(dubId.Value, 1, ct);

        // assert
        stream.Should().NotBeNull();
        heldChunks.Should().BeEmpty("\"Привет,\" is too short to be a clause of its own, and the rest has no boundary");
        chunks.Should().Equal([FakeTranslator.Translated(fragment, Languages.English)],
            "the fragment is the last clause once the source ends");
        var frameCount = await stream!.CountAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(10), ct);
        frameCount.Should().BeGreaterThan(1);
        return;

        void Push(Transcript transcript) {
            source.Writer.TryWrite(transcript - last);
            last = transcript;
        }
    }

    [Fact(Timeout = 90_000)]
    public Task ShortUtteranceShouldBeDubbedOnceTheSourceEnds()
        => AssertShortUtteranceIsDubbedOnceTheSourceEnds(isTagged: true);

    [Fact(Timeout = 90_000)]
    public Task ShortUntaggedUtteranceShouldBeDecidedOnItsTranslationOnceTheSourceEnds()
        => AssertShortUtteranceIsDubbedOnceTheSourceEnds(isTagged: false);

    [Fact(Timeout = 90_000)]
    public Task ShortUtteranceShouldBeDubbedAtTheSegmentEndBeforeTheSourceEnds()
        => AssertShortUtteranceIsDubbedAtTheSegmentEnd(isTagged: true);

    [Fact(Timeout = 90_000)]
    public Task ShortUntaggedUtteranceShouldBeDecidedOnItsTranslationAtTheSegmentEnd()
        => AssertShortUtteranceIsDubbedAtTheSegmentEnd(isTagged: false);

    [Fact(Timeout = 60_000)]
    public async Task ShortUtteranceInTheListenersLanguageShouldNotBeDubbed()
    {
        // arrange - "Yes", tagged English, for an English listener
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.English);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        const string shortText = "Yes";
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var last = Transcript.Empty;
        Push(Unstable(shortText, language: Languages.English));
        await backend.WhenTranscriptPublished(sourceId, ct);
        await Tester.CreateStreamingEntry(chatId, Languages.English, streamId: sourceId.Value, cancellationToken: ct);

        // act - the mix ends only once the worker has decided, so draining it proves the decision
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        Push(Unstable(shortText, language: Languages.English) with { IsStable = true });
        source.Writer.Complete();
        await pushSourceTask.SilentAwait(false);
        var frames = await stream!.ToListAsync(ct);

        // assert - the (absent) original alone: a header and nothing else
        frames.Select(x => x.Offset).Should().Equal([TimeSpan.FromMilliseconds(-1)],
            "the source language decides NoDub once the source ends, however short it is");
        recorder.GetChunks(dubId.Value).Should().BeEmpty("the source is already in the listener's language");
        return;

        void Push(Transcript transcript) {
            source.Writer.TryWrite(transcript - last);
            last = transcript;
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task ListenerShouldHearTheOriginalBeforeAnyDecision()
    {
        // arrange - real audio, no transcript yet
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var sourceId = await Tester.RecordVoiceOnlyUtterance(
            chatId, Languages.Russian, frameCount: 20, cancellationToken: ct);
        var dubId = StreamId.New(sourceId, Languages.English);

        // act
        var startedAt = CpuTimestamp.Now;
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        var servedIn = startedAt.Elapsed;
        var frames = await stream!.ToListAsync(ct);

        // assert - served at once, the whole original came through (header + its frames), nothing held
        servedIn.Should().BeLessThan(TimeSpan.FromSeconds(2));
        frames[0].Offset.Should().Be(TimeSpan.FromMilliseconds(-1), "the first frame is the stream header");
        frames.Count(x => x.Offset >= TimeSpan.Zero).Should().Be(20, "every original frame is mixed through");
    }

    [Fact(Timeout = 60_000)]
    public async Task MidUtteranceJoinerShouldNotHearTheStartOfTheUtterance()
    {
        // arrange - 150 frames of real audio are buffered before anyone asks for the mix
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var sourceId = await Tester.RecordVoiceOnlyUtterance(
            chatId, Languages.Russian, frameCount: 150, cancellationToken: ct);
        var dubId = StreamId.New(sourceId, Languages.English);

        // act - the first request pins the live edge on a fresh mix; a replay request follows
        var live = await backend.GetAudio(dubId, Constants.Audio.SkipToLive, ct);
        var liveFrames = await live!.ToListAsync(ct);
        var whole = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        var wholeFrames = await whole!.ToListAsync(ct);

        // assert - the mix replays the buffered original before the request returns, so the live edge
        // is past it: nothing the original had already buffered is served as live audio
        var liveEdge = Constants.Audio.OpusFrameDuration * 150;
        liveFrames[0].Offset.Should().Be(TimeSpan.FromMilliseconds(-1), "the first frame is the stream header");
        liveFrames.Skip(1).Should().OnlyContain(x => x.Offset >= liveEdge,
            "a joiner must not get the start of the utterance replayed as live audio");
        wholeFrames.Count(x => x.Offset >= TimeSpan.Zero).Should().Be(150, "the mix itself has every frame");
    }

    [Fact(Timeout = 90_000)]
    public async Task DubTailShouldBeDrainedAfterTheSourceEnds()
    {
        // arrange - a transcript-only source (no audio): the mix is dub-only on the tick
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.English);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var last = Transcript.Empty;
        Push(Unstable(SourceSteps[0]));
        await backend.WhenTranscriptPublished(sourceId, ct);
        await Tester.CreateStreamingEntry(chatId, Languages.Russian, streamId: sourceId.Value, cancellationToken: ct);

        // act
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        Push(Stable(SourceText));
        source.Writer.Complete();
        await pushSourceTask.SilentAwait(false);
        var chunks = await recorder.WhenSpoken(dubId.Value, 1, ct);
        var frames = await stream!.ToListAsync(ct);

        // assert - the fake speaks one frame per four characters; the mix carries them all
        chunks.Should().NotBeEmpty();
        frames.Count(x => x.Offset >= TimeSpan.Zero).Should().BeGreaterThanOrEqualTo(
            chunks.Sum(x => Math.Max(1, x.Length / 4)),
            "the dub tail is drained in full after the original ended");
        return;

        void Push(Transcript transcript) {
            source.Writer.TryWrite(transcript - last);
            last = transcript;
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task SourceInTheListenersLanguageShouldBeServedAsTheOriginalOnly()
    {
        // arrange - real audio with an English transcript the test keeps open (a recording's own
        // transcript is dropped once its transcription ends), so the source language decides NoDub
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var sourceId = await Tester.RecordVoiceOnlyUtterance(
            chatId, Languages.English, frameCount: 150, cancellationToken: ct);
        var dubId = StreamId.New(sourceId, Languages.English);
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        source.Writer.TryWrite(Unstable("Hi, how are you today?", language: Languages.English) - Transcript.Empty);
        await backend.WhenTranscriptPublished(sourceId, ct);

        // act - drained while the source is still open: only a source-language decision ends the
        // mix here, a translation-based one would wait for the source to end
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        var drainStartedAt = CpuTimestamp.Now;
        var frames = await stream!.ToListAsync(ct);
        var drainedIn = drainStartedAt.Elapsed;
        source.Writer.Complete();
        await pushSourceTask.SilentAwait(false);

        // assert - the mix is the original alone and ends with it
        frames.Count(x => x.Offset >= TimeSpan.Zero).Should().Be(150, "every original frame, nothing after");
        recorder.GetChunks(dubId.Value).Should().BeEmpty("nothing was synthesized");
        drainedIn.Should().BeLessThan(TimeSpan.FromSeconds(3.5),
            "NoDub ends the mix at once, while a transcript miss takes two share waits (4 s)");
    }

    [Fact(Timeout = 90_000)]
    public async Task DubShouldBeDecidedOnTheSourceLanguageBeforeAnyTranslation()
    {
        // arrange - no entry yet, so nothing is translated while the source is unstable
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.English);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var last = Transcript.Empty;
        Push(Unstable(SourceSteps[0]));
        await backend.WhenTranscriptPublished(sourceId, ct);

        // act
        var requestedAt = CpuTimestamp.Now;
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        await recorder.WhenStarted(dubId.Value, ct);
        var decidedIn = requestedAt.Elapsed;
        var chunksBeforeTranslation = recorder.GetChunks(dubId.Value);
        await Tester.CreateStreamingEntry(chatId, Languages.Russian, streamId: sourceId.Value, cancellationToken: ct);
        Push(Stable(SourceText));
        var chunks = await recorder.WhenSpoken(dubId.Value, 1, ct);
        source.Writer.Complete();
        await pushSourceTask.SilentAwait(false);

        // assert
        stream.Should().NotBeNull("a Russian source of 10+ chars decides the dub for an English listener");
        // Two of the seconds are the share wait for the audio a transcript-only source never has
        decidedIn.Should().BeLessThan(TimeSpan.FromSeconds(5), "the decision waits for no translation");
        chunksBeforeTranslation.Should().BeEmpty("the synthesizer is opened before there is anything to say");
        chunks.Should().Equal([FakeTranslator.Translated(SourceText, Languages.English)]);
        return;

        void Push(Transcript transcript) {
            source.Writer.TryWrite(transcript - last);
            last = transcript;
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task SourceInTheListenersLanguageShouldNotBeDubbedBeforeAnyTranslation()
    {
        // arrange - no entry, no translation: the source language alone answers
        await Tester.SignInAsUniqueAlice();
        await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.Russian);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        source.Writer.TryWrite(Unstable(SourceSteps[0]) - Transcript.Empty);
        await backend.WhenTranscriptPublished(sourceId, ct);

        // act - drained while the source is still open: only a source-language decision ends the
        // mix here, a translation-based one would wait for the source to end
        var requestedAt = CpuTimestamp.Now;
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        var servedIn = requestedAt.Elapsed;
        var frames = await stream!.ToListAsync(ct);
        source.Writer.Complete();
        await pushSourceTask.SilentAwait(false);

        // assert - the mix ends with the (absent) original: a header and nothing else. Two of the
        // seconds are the share wait for the audio a transcript-only source never has
        servedIn.Should().BeLessThan(TimeSpan.FromSeconds(5), "the mix is served before any decision");
        frames.Select(x => x.Offset).Should().Equal([TimeSpan.FromMilliseconds(-1)],
            "the source language alone decides, before any translation");
        recorder.GetChunks(dubId.Value).Should().BeEmpty("the source is already in the listener's language");
    }

    [Fact(Timeout = 60_000)]
    public async Task LateTranscriptAfterAudioEndShouldStillBeDubbed()
    {
        // arrange - the audio ends with nothing published yet; the transcript that follows lands
        // after WaitForSourceTranscript has already seen the audio end, inside its one grace pass
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var sourceId = await Tester.RecordVoiceOnlyUtterance(chatId, Languages.Russian, cancellationToken: ct);
        var dubId = StreamId.New(sourceId, Languages.English);

        // act - the two ShareWaitDelay cycles WaitForSourceTranscript spends noticing the audio
        // ended and then giving it one more pass span roughly [2s, 4s); publish in the middle of
        // that window for margin against Task.Delay running long under load. The transcript stream
        // is registered only once PushTranscript is called (not once any content is written to
        // it), so starting that call itself is what has to land late here.
        var getAudioTask = backend.GetAudio(dubId, TimeSpan.Zero, ct);
        await Task.Delay(TimeSpan.FromSeconds(3), ct);
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        source.Writer.TryWrite(Stable(SourceText) - Transcript.Empty);
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        await backend.WhenTranscriptPublished(sourceId, ct);
        await Tester.CreateStreamingEntry(chatId, Languages.Russian, streamId: sourceId.Value, cancellationToken: ct);

        // assert
        var stream = await getAudioTask;
        stream.Should().NotBeNull("a transcript arriving shortly after the audio ends must still be dubbed");
        var chunks = await recorder.WhenSpoken(dubId.Value, 1, ct);
        chunks.Should().Equal([FakeTranslator.Translated(SourceText, Languages.English)]);

        source.Writer.Complete();
        await pushSourceTask.SilentAwait(false);
    }

    [Fact(Timeout = 90_000)]
    public async Task DubShouldSpeakInTheSpeakersVoice()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.English);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;
        await services.UserSettingsUI(Tester.Session).UserLanguageSettings()
            .Update(x => x with { DubVoice = "Daniel" }, ct);
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var last = Transcript.Empty;
        Push(Unstable(SourceSteps[0]));
        await backend.WhenTranscriptPublished(sourceId, ct);
        var entry = await Tester.CreateStreamingEntry(
            chatId, Languages.Russian, streamId: sourceId.Value, cancellationToken: ct);
        // ProcessAudio fills the author map for a real recording; a pushed transcript has to do it here
        var streamingBackend = (AudioStreamingBackend)backend;
        streamingBackend.RememberChatId(sourceId, chatId);
        streamingBackend.RememberAuthorId(sourceId, entry.ChatEntrySlim.AuthorId);
        streamingBackend.RememberRecordedAt(sourceId, services.Clocks().ServerClock.Now);

        // act
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        Push(Stable(SourceText));
        var chunks = await recorder.WhenSpoken(dubId.Value, 1, ct);
        source.Writer.Complete();
        await pushSourceTask.SilentAwait(false);

        // assert
        stream.Should().NotBeNull();
        chunks.Should().HaveCount(1);
        recorder.GetVoiceId(dubId.Value).Should().Be("Daniel", "the live dub is spoken in the speaker's voice");
        return;

        void Push(Transcript transcript) {
            source.Writer.TryWrite(transcript - last);
            last = transcript;
        }
    }

    [Fact(Timeout = 90_000)]
    public async Task DubShouldSpeakInTheSpeakersClonedVoice()
    {
        // arrange
        var account = await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var pool = services.GetRequiredService<VoicePool>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.English);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;
        await Tester.OptInOwnVoice(chatId, Languages.Russian);
        // The pool never waits for a clone, so the dub that asks first speaks with the stock voice:
        // this one is made before the dub, as an earlier utterance would have done
        var cloneVoiceId = await pool.AcquireSettled(account.Id, ct);
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var last = Transcript.Empty;
        Push(Unstable(SourceSteps[0]));
        await backend.WhenTranscriptPublished(sourceId, ct);
        var entry = await Tester.CreateStreamingEntry(
            chatId, Languages.Russian, streamId: sourceId.Value, cancellationToken: ct);
        // ProcessAudio fills the author map for a real recording; a pushed transcript has to do it here
        var streamingBackend = (AudioStreamingBackend)backend;
        streamingBackend.RememberChatId(sourceId, chatId);
        streamingBackend.RememberAuthorId(sourceId, entry.ChatEntrySlim.AuthorId);
        streamingBackend.RememberRecordedAt(sourceId, services.Clocks().ServerClock.Now);

        // act
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        Push(Stable(SourceText));
        var chunks = await recorder.WhenSpoken(dubId.Value, 1, ct);
        source.Writer.Complete();
        await pushSourceTask.SilentAwait(false);

        // assert
        stream.Should().NotBeNull();
        chunks.Should().HaveCount(1);
        cloneVoiceId.Should().NotBeNullOrEmpty("an opted-in speaker with a sample gets a clone");
        recorder.GetVoiceId(dubId.Value).Should().Be(cloneVoiceId,
            "the live dub is spoken in the speaker's cloned voice");
        return;

        void Push(Transcript transcript) {
            source.Writer.TryWrite(transcript - last);
            last = transcript;
        }
    }

    [Fact(Timeout = 90_000)]
    public async Task NextUtteranceShouldStartDuckedWhileThePreviousDubSpeaks()
    {
        // arrange - utterance 1 is a transcript with a long dub (the fake speaks one frame per four
        // characters), still draining when utterance 2, real audio by the same author, is mixed
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var first = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var firstDub = StreamId.New(first, Languages.English);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;
        var longText = string.Join(" ", Enumerable.Repeat("Привет, как у тебя дела?", 60));
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(first, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var last = Transcript.Empty;
        Push(Unstable(SourceSteps[0]));
        await backend.WhenTranscriptPublished(first, ct);
        var entry = await Tester.CreateStreamingEntry(
            chatId, Languages.Russian, streamId: first.Value, cancellationToken: ct);
        // The activity is the author's: a pushed transcript has to name one, as ProcessAudio does for a recording
        var streamingBackend = (AudioStreamingBackend)backend;
        streamingBackend.RememberChatId(first, chatId);
        streamingBackend.RememberAuthorId(first, entry.ChatEntrySlim.AuthorId);
        var firstStream = await backend.GetAudio(firstDub, TimeSpan.Zero, ct);
        Push(Stable(longText));
        source.Writer.Complete();
        await pushSourceTask.SilentAwait(false);
        await recorder.WhenSpoken(firstDub.Value, 1, ct);

        // act - utterance 2 is requested while dub 1 speaks
        var second = await Tester.RecordVoiceOnlyUtterance(
            chatId, Languages.Russian, frameCount: 50, cancellationToken: ct);
        var secondDub = StreamId.New(second, Languages.English);
        var secondStream = await backend.GetAudio(secondDub, TimeSpan.Zero, ct);
        var secondFrames = await secondStream!.ToListAsync(ct);
        var firstFrames = await firstStream!.ToListAsync(ct);

        // assert - both were served; the second's original came through ducked, quieter than the same
        // recording served plain
        firstFrames.Count(x => x.Offset >= TimeSpan.Zero).Should().BeGreaterThan(300, "the long dub was drained");
        var mixed = secondFrames.Where(x => x.Offset >= TimeSpan.Zero).ToList();
        mixed.Should().HaveCount(50, "every original frame is mixed through");
        var plain = await backend.GetAudio(second, TimeSpan.Zero, ct);
        var plainFrames = (await plain!.ToListAsync(ct)).Where(x => x.Offset >= TimeSpan.Zero).ToList();
        AudioFrameRms.Of(mixed).Should().BeLessThan(AudioFrameRms.Of(plainFrames) * 0.6,
            "the next utterance of the author starts ducked while their previous dub is still speaking");
        return;

        void Push(Transcript transcript) {
            source.Writer.TryWrite(transcript - last);
            last = transcript;
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task SynthesisFailureShouldLeaveTheOriginalPlaying()
    {
        // arrange - real audio with a Russian transcript the test keeps live (a recording's own
        // transcript is dropped once its transcription ends), so the dub is decided and synthesized
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var sourceId = await Tester.RecordVoiceOnlyUtterance(
            chatId, Languages.Russian, frameCount: 150, cancellationToken: ct);
        var dubId = StreamId.New(sourceId, Languages.English);
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var last = Transcript.Empty;
        Push(Unstable(SourceSteps[0]));
        await backend.WhenTranscriptPublished(sourceId, ct);
        await Tester.CreateStreamingEntry(chatId, Languages.Russian, streamId: sourceId.Value, cancellationToken: ct);
        recorder.FailWith = streamId => streamId == dubId.Value ? StandardError.External("TTS is down.") : null;
        try {
            // act
            var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
            await recorder.WhenStarted(dubId.Value, ct);
            Push(Stable(SourceText));
            source.Writer.Complete();
            await pushSourceTask.SilentAwait(false);
            var frames = await stream!.ToListAsync(ct);
            var again = await backend.GetAudio(dubId, TimeSpan.Zero, ct);

            // assert - the mix ended cleanly with the whole original, and the backend serves on
            frames.Count(x => x.Offset >= TimeSpan.Zero).Should().Be(150,
                "the original plays on when its dub's synthesis fails");
            recorder.GetChunks(dubId.Value).Should().BeEmpty("the synthesis failed before it was given any text");
            if (again != null)
                (await again.ToListAsync(ct)).Should().HaveCount(frames.Count, "the finished mix is served as is");
        }
        finally {
            recorder.FailWith = null;
            ((AudioStreamingBackend)backend).ForgetSynthesizerFailure();
        }
        return;

        void Push(Transcript transcript) {
            source.Writer.TryWrite(transcript - last);
            last = transcript;
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task ShortUtteranceFollowedAtOnceShouldBeHeardInFull()
    {
        // arrange - 0.6 s of real audio with the same author's next utterance right behind it. This
        // goes through the backend's GetAudio, so it proves only that two back-to-back pass-through
        // mixes of one author lose no frames there: voice-only sources never reach the dub chain, and
        // the muxer's per-author "latest wins" (the original bug) is guarded by
        // ListeningStreamMuxerTest.TryRegisterShouldNotMergeDubbedStreamsOfTheSameAuthor
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var first = await Tester.RecordVoiceOnlyUtterance(
            chatId, Languages.Russian, frameCount: 30, cancellationToken: ct);
        var firstDub = StreamId.New(first, Languages.English);

        // act
        var firstStream = await backend.GetAudio(firstDub, TimeSpan.Zero, ct);
        var second = await Tester.RecordVoiceOnlyUtterance(
            chatId, Languages.Russian, frameCount: 30, cancellationToken: ct);
        var secondDub = StreamId.New(second, Languages.English);
        var secondStream = await backend.GetAudio(secondDub, TimeSpan.Zero, ct);
        var firstFrames = await firstStream!.ToListAsync(ct);
        var secondFrames = await secondStream!.ToListAsync(ct);

        // assert
        firstFrames.Count(x => x.Offset >= TimeSpan.Zero).Should().Be(30,
            "every frame of the short utterance is played, none is swallowed by the next one");
        secondFrames.Count(x => x.Offset >= TimeSpan.Zero).Should().Be(30, "the next one is heard in full as well");
    }

    // Private methods

    private async Task AssertShortUtteranceIsDubbedOnceTheSourceEnds(bool isTagged)
    {
        // arrange - "Да" is under DubStabilizer.MinDecisionLength, so nothing decides while it's live;
        // the translation is as short as a real one ("Yes"), which the early gate holds back too
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var translator = FakeTranslator.Realtime(services);
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.English);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;
        const string shortText = "Да";
        const string shortTranslation = "Yes";
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var last = Transcript.Empty;
        translator.Respond = (text, language) => text.Trim() == shortText
            ? shortTranslation
            : FakeTranslator.Translated(text, language);
        try {
            Push(Unstable(shortText));
            await backend.WhenTranscriptPublished(sourceId, ct);
            await Tester.CreateStreamingEntry(
                chatId, Languages.Russian, streamId: sourceId.Value, cancellationToken: ct);

            // act - the source ends with the short utterance as its whole text
            var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
            Push(Stable(shortText));
            source.Writer.Complete();
            await pushSourceTask.SilentAwait(false);
            var chunks = await recorder.WhenSpoken(dubId.Value, 1, ct);

            // assert
            stream.Should().NotBeNull();
            chunks.Should().Equal([shortTranslation], isTagged
                ? "a Russian-tagged utterance too short for an early call is dubbed once the source ends"
                : "a translation that differs from the source decides the dub once nothing more is coming");
            var snapshot = await backend.GetTranscriptSnapshot(sourceId, ct);
            snapshot!.TimeRange.End.Should().BeApproximately(last.TimeRange.End, 0.001f,
                "the source fold the dub reads its end from must not drop to zero once the source has ended");
            var frameCount = await stream!.CountAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(10), ct);
            frameCount.Should().BeGreaterThan(1);
        }
        finally {
            translator.Respond = FakeTranslator.Translated;
        }
        return;

        void Push(Transcript transcript) {
            if (!isTagged)
                transcript = transcript with { Languages = [] };
            source.Writer.TryWrite(transcript - last);
            last = transcript;
        }
    }

    private async Task AssertShortUtteranceIsDubbedAtTheSegmentEnd(bool isTagged)
    {
        // arrange - the transcriber's endpoint comes seconds before the recorder stops sending the
        // trailing silence that ends the source; "Да" is under DubStabilizer.MinDecisionLength
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var translator = FakeTranslator.Realtime(services);
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.English);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;
        const string shortText = "Да";
        const string shortTranslation = "Yes";
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var last = Transcript.Empty;
        translator.Respond = (text, language) => text.Trim() == shortText
            ? shortTranslation
            : FakeTranslator.Translated(text, language);
        try {
            Push(Unstable(shortText));
            await backend.WhenTranscriptPublished(sourceId, ct);
            await Tester.CreateStreamingEntry(
                chatId, Languages.Russian, streamId: sourceId.Value, cancellationToken: ct);

            // act - the segment ends, the source doesn't
            var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
            Push(Stable(shortText) with { IsSegmentEnd = true });
            var chunks = await recorder.WhenSpoken(dubId.Value, 1, ct);

            // assert
            stream.Should().NotBeNull();
            chunks.Should().Equal([shortTranslation], isTagged
                ? "a Russian-tagged utterance the transcriber heard out is dubbed at its segment end"
                : "a translation that differs from the source decides the dub at the segment end");
            source.Writer.Complete();
            await pushSourceTask.SilentAwait(false);
            var frameCount = await stream!.CountAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(10), ct);
            frameCount.Should().BeGreaterThan(1);
        }
        finally {
            source.Writer.TryComplete();
            translator.Respond = FakeTranslator.Translated;
        }
        return;

        void Push(Transcript transcript) {
            if (!isTagged)
                transcript = transcript with { Languages = [] };
            source.Writer.TryWrite(transcript - last);
            last = transcript;
        }
    }

    private async Task AssertDubSpeaksTheTranslation(
        TimeSpan entryDelay,
        bool mustRequestBeforeEntry,
        float backlogSeconds = 0)
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.English);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;

        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var last = Transcript.Empty;
        // The translation runs per clause, so what a late listener skips is whole clauses: the
        // backlog is two sentences, unstable but complete, and the speaker goes on with a third
        const string secondSentence = " Хорошо, спасибо.";
        const string thirdSentence = " До встречи!";
        var isLate = backlogSeconds > 0;
        var backlog = Unstable(isLate ? SourceText + secondSentence : SourceSteps[0], backlogSeconds);
        var fullText = isLate ? backlog.Text + thirdSentence : SourceText;
        Push(backlog);
        await backend.WhenTranscriptPublished(sourceId, ct);

        // act
        var createEntryTask = BackgroundTask.Run(async () => {
            await Task.Delay(entryDelay, ct);
            await Tester.CreateStreamingEntry(
                chatId, Languages.Russian, streamId: sourceId.Value, cancellationToken: ct);
        }, ct);
        if (!mustRequestBeforeEntry)
            await createEntryTask;
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        await createEntryTask;
        var captions = await backend.GetTranscript(dubId, ct);
        if (isLate) {
            // The dub is decided on the source, before the translation has read it; the backlog is
            // translated before the speaker goes on. Whether the dub then sees it as transcripts of
            // its own or folded into the third sentence depends on when it attaches to the
            // translation, and the skip must cut the backlog off either way. Real time maps keep
            // the backlog's end where it was: the later transcripts extend the backlog's map rather
            // than re-timing it.
            await WhenTranslated(captions!, secondSentence, ct);
            await recorder.WhenStarted(dubId.Value, ct);
            Push(backlog.WithSuffix(thirdSentence[..^5], backlogSeconds + 2));
            Push(backlog.WithSuffix(thirdSentence, backlogSeconds + 3) with { IsStable = true });
        }
        else {
            foreach (var step in SourceSteps.Skip(1))
                Push(Unstable(step));
            Push(Stable(fullText));
        }

        // assert
        stream.Should().NotBeNull("a Russian speaker is dubbed for an English listener");
        captions.Should().NotBeNull("the caption reader must get the same translated stream");
        var chunks = await recorder.WhenSpoken(dubId.Value, 1, ct);
        if (isLate)
            chunks.Should().Equal([$" {FakeTranslator.Translated(thirdSentence, Languages.English)}"],
                "a listener who joined seconds into the utterance hears only the clause said after that");
        else
            chunks.Should().Equal([FakeTranslator.Translated(SourceText, Languages.English)],
                "only the stable translation is spoken, and once");

        source.Writer.Complete();
        await pushSourceTask.SilentAwait(false);
        var frameCount = await stream!.CountAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(10), ct);
        frameCount.Should().BeGreaterThan(1,
            "the dub ends once the source has ended and its last translation is spoken, not when the "
            + "entry is finalized, or the author's next dub would wait for the re-transcription");
        return;

        void Push(Transcript transcript) {
            source.Writer.TryWrite(transcript - last);
            last = transcript;
        }
    }

    private static async Task WhenTranslated(
        RpcStream<TranscriptDiff> captions,
        string sourceText,
        CancellationToken cancellationToken)
    {
        var translated = FakeTranslator.Translated(sourceText, Languages.English);
        var folded = Transcript.Empty;
        await foreach (var diff in captions.WithCancellation(cancellationToken)) {
            folded += diff;
            if (folded.Text.Contains(translated))
                return;
        }
        throw new InvalidOperationException($"The captions ended without '{translated}'.");
    }

    private static Transcript Unstable(string text, float endTime = 0, Language? language = null)
    {
        // ~10 chars per second of speech unless the test asks for a specific backlog
        if (endTime <= 0)
            endTime = text.Length * 0.1f;
        var timeMap = LinearMap.Zero.Append(new Vector2(text.Length, endTime));
        return new Transcript(text, timeMap, [language ?? Languages.Russian]);
    }

    private static Transcript Stable(string text)
        => Unstable(text) with { IsStable = true };
}

[CollectionDefinition(nameof(DubbingTranslationCollection))]
public sealed class DubbingTranslationCollection : ICollectionFixture<DubbingTranslationCollection.AppHostFixture>
{
    public sealed class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture(
            "dubbing-translation",
            messageSink,
            TestAppHostOptions.Default with {
                ConfigureHost = (_, cfg) => {
                    cfg.AddInMemory<ChatSettings>((x => x.IsTranslationEnabled, "true"));
                    cfg.AddInMemory<CoreServerSettings>((x => x.OpenAIKey, "test-key"));
                    cfg.AddInMemory<TranscriptionSettings>((x => x.UseFakeTranscriber, "true"));
                },
                ConfigureServices = (_, services) => {
                    services.AddSingleton<Translator>(c => new FakeTranslator(c));
                    services.AddKeyedSingleton<Translator>(
                        Constants.Translation.RealtimeServiceKey,
                        (c, key) => new FakeTranslator(c, (string)key));
                    services.AddSingleton<RecordingSpeechSynthesizer>();
                    services.AddSingleton<ISpeechSynthesizer>(c => c.GetRequiredService<RecordingSpeechSynthesizer>());
                },
            });
}
