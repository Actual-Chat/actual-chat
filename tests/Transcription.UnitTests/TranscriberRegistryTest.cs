using ActualChat.Audio;
using ActualChat.Transcription.Module;

namespace ActualChat.Transcription.UnitTests;

public class TranscriberRegistryTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ListStreamFollowsRankingOrder()
    {
        // arrange
        var registry = NewRegistry(
            new TranscriptionSettings { StreamRanking = "b-stream,a-stream" },
            NewStream("a-stream"),
            NewStream("b-stream"));

        // act
        var chain = registry.ListStream(new TranscriptionOptions(), TranscriberId.None);

        // assert
        chain.Select(x => x.Info.Id.Value).Should().Equal("b-stream", "a-stream");
    }

    [Fact]
    public void ListStreamSkipsUnregisteredIds()
    {
        // arrange
        var registry = NewRegistry(
            new TranscriptionSettings { StreamRanking = "missing-stream,a-stream" },
            NewStream("a-stream"));

        // act
        var chain = registry.ListStream(new TranscriptionOptions(), TranscriberId.None);

        // assert
        chain.Select(x => x.Info.Id.Value).Should().Equal("a-stream");
    }

    [Fact]
    public void ListStreamFiltersByDeclaredLanguages()
    {
        // arrange
        var registry = NewRegistry(
            new TranscriptionSettings { StreamRanking = "en-only,any-language" },
            NewStream("en-only", languages: [Languages.English]),
            NewStream("any-language"));

        // act
        var english = registry.ListStream(new TranscriptionOptions { Language = Languages.English }, TranscriberId.None);
        var russian = registry.ListStream(new TranscriptionOptions { Language = Languages.Russian }, TranscriberId.None);

        // assert
        english.Select(x => x.Info.Id.Value).Should().Equal("en-only", "any-language");
        russian.Select(x => x.Info.Id.Value).Should().Equal("any-language");
    }

    [Fact]
    public void ListStreamRequiresDetectionSupportInDetectMode()
    {
        // arrange
        var registry = NewRegistry(
            new TranscriptionSettings { StreamRanking = "no-detect,detects" },
            NewStream("no-detect"),
            NewStream("detects", isLanguageDetectionSupported: true));
        var options = TranscriptionOptions.AutoDetectLanguage([Languages.English, Languages.Russian], null);

        // act
        var chain = registry.ListStream(options, TranscriberId.None);

        // assert
        chain.Select(x => x.Info.Id.Value).Should().Equal("detects");
    }

    [Fact]
    public void PerLanguageOverrideWinsOverDefaultRanking()
    {
        // arrange
        var settings = new TranscriptionSettings {
            StreamRanking = "a-stream,b-stream",
            StreamRankingOverrides = { ["ru-RU"] = "b-stream" },
        };
        var registry = NewRegistry(settings, NewStream("a-stream"), NewStream("b-stream"));

        // act
        var english = registry.ListStream(new TranscriptionOptions { Language = Languages.English }, TranscriberId.None);
        var russian = registry.ListStream(new TranscriptionOptions { Language = Languages.Russian }, TranscriberId.None);

        // assert
        english.Select(x => x.Info.Id.Value).Should().Equal("a-stream", "b-stream");
        russian.Select(x => x.Info.Id.Value).Should().Equal("b-stream");
    }

    [Fact]
    public void PinnedTranscriberIsTheWholeChain()
    {
        // arrange
        var registry = NewRegistry(
            new TranscriptionSettings { StreamRanking = "a-stream,b-stream" },
            NewStream("a-stream"),
            NewStream("b-stream"));

        // act
        var pinned = registry.ListStream(new TranscriptionOptions(), TranscriberId.NewBuiltin("b-stream"));
        var pinnedMissing = registry.ListStream(new TranscriptionOptions(), TranscriberId.NewBuiltin("nope"));

        // assert
        pinned.Select(x => x.Info.Id.Value).Should().Equal("b-stream");
        pinnedMissing.Should().BeEmpty();
    }

    [Fact]
    public void StreamAndOfflineChainsDoNotMix()
    {
        // arrange
        var registry = NewRegistry(
            new TranscriptionSettings { StreamRanking = "a-stream", OfflineRanking = "a-offline" },
            [NewStream("a-stream")],
            [NewOffline("a-offline")]);

        // act
        var streams = registry.ListStream(new TranscriptionOptions(), TranscriberId.None);
        var offlines = registry.ListOffline(new TranscriptionOptions(), TranscriberId.None);

        // assert
        streams.Select(x => x.Info.Id.Value).Should().Equal("a-stream");
        offlines.Select(x => x.Info.Id.Value).Should().Equal("a-offline");
    }

    // Private methods

    private static TranscriberRegistry NewRegistry(TranscriptionSettings settings, params ITranscriber[] streams)
        => NewRegistry(settings, streams, []);

    private static TranscriberRegistry NewRegistry(
        TranscriptionSettings settings,
        ITranscriber[] streams,
        IOfflineTranscriber[] offlines)
        => new(settings, streams, offlines);

    private static TestTranscriber NewStream(
        string key,
        Language[]? languages = null,
        bool isLanguageDetectionSupported = false)
        => new(new TranscriberInfo {
            Id = TranscriberId.NewBuiltin(key),
            Kind = TranscriberKind.Stream,
            Languages = languages == null ? new ApiSet<Language>() : new ApiSet<Language>(languages),
            IsLanguageDetectionSupported = isLanguageDetectionSupported,
        });

    private static TestOfflineTranscriber NewOffline(string key)
        => new(new TranscriberInfo {
            Id = TranscriberId.NewBuiltin(key),
            Kind = TranscriberKind.Offline,
        });

    // Nested types

    private sealed class TestTranscriber(TranscriberInfo info) : ITranscriber
    {
        public TranscriberInfo Info { get; } = info;

        public Task Transcribe(
            string audioStreamId,
            AudioSource audioSource,
            TranscriptionOptions options,
            ChannelWriter<Transcript> output,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class TestOfflineTranscriber(TranscriberInfo info) : IOfflineTranscriber
    {
        public TranscriberInfo Info { get; } = info;

        public Task<Transcript?> Transcribe(
            AudioSource audioSource,
            TranscriptionOptions options,
            CancellationToken cancellationToken = default)
            => Task.FromResult((Transcript?)null);
    }
}
