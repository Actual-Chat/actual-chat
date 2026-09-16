using System.Numerics;
using ActualChat.Chat.Module;
using ActualChat.Module;
using ActualChat.Streaming;
using ActualChat.Streaming.Services;
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

    [Fact(Timeout = 60_000)]
    public async Task SourceWithoutTranscriptShouldFallBackAtOnce()
    {
        // arrange - a short utterance whose audio ends before any transcript is published
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var sourceId = await Tester.RecordVoiceOnlyUtterance(chatId, Languages.Russian, cancellationToken: ct);
        var dubId = StreamId.New(sourceId, Languages.English);

        // act
        var startedAt = CpuTimestamp.Now;
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);

        // assert - the original, well inside DubWaitTimeout
        stream.Should().BeNull("there is nothing to dub");
        startedAt.Elapsed.Should().BeLessThan(Constants.Audio.DubWaitTimeout / 2);
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

    // Private methods

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
        var backlog = Unstable(SourceSteps[0], backlogSeconds);
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
        foreach (var step in SourceSteps.Skip(1))
            Push(Unstable(step));
        Push(Stable(SourceText));

        // assert
        stream.Should().NotBeNull("a Russian speaker is dubbed for an English listener");
        var chunks = await recorder.WhenSpoken(dubId.Value, 1, ct);
        var translation = FakeTranslator.Translated(SourceText, Languages.English);
        var expectedChunk = backlogSeconds > 0
            ? translation[FakeTranslator.Translated(backlog.Text, Languages.English).Length..]
            : translation;
        chunks.Should().Equal([expectedChunk], backlogSeconds > 0
            ? "a listener who joined seconds into the utterance hears only what was said after that"
            : "only the stable translation is spoken, and once");
        var captions = await backend.GetTranscript(dubId, ct);
        captions.Should().NotBeNull("the caption reader must get the same translated stream");

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

    private static Transcript Unstable(string text, float endTime = 0)
    {
        // ~10 chars per second of speech unless the test asks for a specific backlog
        if (endTime <= 0)
            endTime = text.Length * 0.1f;
        return new Transcript(text, LinearMap.Zero.Append(new Vector2(text.Length, endTime)), [Languages.Russian]);
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
