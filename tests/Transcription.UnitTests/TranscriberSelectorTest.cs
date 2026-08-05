using ActualChat.Audio;
using ActualChat.Transcription.Module;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Transcription.UnitTests;

public sealed class TranscriberSelectorTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string Stream = "a-stream";
    private const string Offline = "a-offline";
    private const string OtherOffline = "b-offline";

    [Fact]
    public void AutomaticRetranscribesEvenASelfRefinedStream()
    {
        // arrange
        var selector = NewSelector(TranscriberKind.StreamSelfRefined);

        // act
        var offline = selector.GetOffline(new TranscriptionOptions(), TranscriberId.None);

        // assert
        offline?.Info.Id.Value.Should().Be(Offline);
    }

    [Fact]
    public void ExplicitSelfRefinedStreamSkipsRetranscription()
    {
        // arrange
        var selector = NewSelector(TranscriberKind.StreamSelfRefined, out var registry);
        var id = TranscriberId.NewBuiltin(Stream);

        // act
        var offline = selector.GetOffline(new TranscriptionOptions(), id, registry.GetInfo(id));

        // assert
        offline.Should().BeNull();
    }

    [Fact]
    public void ExplicitStreamOnlySkipsRetranscriptionToo()
    {
        // arrange
        // A StreamOnly stream benefits from an offline pass, but "no re-transcription" in the
        // picker has to mean it - this is what silently refined ElevenLabs with Soniox.
        var selector = NewSelector(TranscriberKind.StreamOnly, out var registry);
        var id = TranscriberId.NewBuiltin(Stream);

        // act
        var offline = selector.GetOffline(new TranscriptionOptions(), id, registry.GetInfo(id));

        // assert
        offline.Should().BeNull();
    }

    [Fact]
    public void ExplicitPairOverridesTheSelfRefinedSkip()
    {
        // arrange
        var selector = NewSelector(TranscriberKind.StreamSelfRefined, out var registry);
        var id = TranscriberId.NewPair(TranscriberId.NewBuiltin(Stream), TranscriberId.NewBuiltin(Offline));

        // act
        var offline = selector.GetOffline(new TranscriptionOptions(), id, registry.GetInfo(id));

        // assert
        offline?.Info.Id.Value.Should().Be(Offline);
    }

    [Fact]
    public void ExplicitPairTakesTheNamedRetranscriberOverTheRanking()
    {
        // arrange
        var selector = NewSelector(TranscriberKind.StreamSelfRefined, out var registry);
        var id = TranscriberId.NewPair(TranscriberId.NewBuiltin(Stream), TranscriberId.NewBuiltin(OtherOffline));

        // act
        var offline = selector.GetOffline(new TranscriptionOptions(), id, registry.GetInfo(id));

        // assert
        offline?.Info.Id.Value.Should().Be(OtherOffline);
    }

    // Private methods

    private static TranscriberSelector NewSelector(TranscriberKind kind)
        => NewSelector(kind, out _);

    private static TranscriberSelector NewSelector(TranscriberKind kind, out TranscriberRegistry registry)
    {
        var settings = new TranscriptionSettings {
            StreamRanking = Stream,
            OfflineRanking = $"{Offline},{OtherOffline}",
        };
        registry = new TranscriberRegistry(
            settings,
            [new TestTranscriber(new TranscriberInfo {
                Id = TranscriberId.NewBuiltin(Stream),
                DriverId = "test",
                Kind = kind,
            })],
            [
                NewOffline(Offline),
                NewOffline(OtherOffline),
            ]);
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();

        return new TranscriberSelector(registry, services);
    }

    private static TestOfflineTranscriber NewOffline(string key)
        => new(new TranscriberInfo {
            Id = TranscriberId.NewBuiltin(key),
            DriverId = "test",
            Kind = TranscriberKind.OfflineOnly,
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
