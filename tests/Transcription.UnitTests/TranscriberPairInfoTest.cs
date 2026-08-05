using ActualChat.Audio;
using ActualChat.Transcription.Module;

namespace ActualChat.Transcription.UnitTests;

public class TranscriberPairInfoTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void PairLanguagesAreTheIntersection()
    {
        // arrange
        var registry = NewRegistry(
            NewStream(TranscriberId.DeepgramStream, [Languages.English, Languages.Russian, Languages.German]),
            NewOffline(TranscriberId.OpenAIOffline, [Languages.Russian, Languages.German, Languages.French]));

        // act
        var info = registry.GetInfo(TranscriberId.NewPair(TranscriberId.DeepgramStream, TranscriberId.OpenAIOffline));

        // assert
        info.Should().NotBeNull();
        info!.Languages.Should().BeEquivalentTo([Languages.Russian, Languages.German]);
        info.Retranscriber!.Id.Should().Be(TranscriberId.OpenAIOffline);
    }

    [Fact]
    public void AnEmptySetDoesNotNarrowThePair()
    {
        // arrange - OpenAI declares no languages, meaning "no restriction"
        var registry = NewRegistry(
            NewStream(TranscriberId.DeepgramStream, [Languages.English, Languages.Russian]),
            NewOffline(TranscriberId.OpenAIOffline, []));

        // act
        var info = registry.GetInfo(TranscriberId.NewPair(TranscriberId.DeepgramStream, TranscriberId.OpenAIOffline));

        // assert
        info!.Languages.Should().BeEquivalentTo([Languages.English, Languages.Russian]);
    }

    [Fact]
    public void PairIsNullWhenAHalfIsMissing()
    {
        // arrange
        var registry = NewRegistry(NewStream(TranscriberId.DeepgramStream, []), null);

        // act
        var info = registry.GetInfo(TranscriberId.NewPair(TranscriberId.DeepgramStream, TranscriberId.OpenAIOffline));

        // assert
        info.Should().BeNull();
    }

    // Private methods

    private static TranscriberRegistry NewRegistry(ITranscriber stream, IOfflineTranscriber? offline)
        => new(new TranscriptionSettings(), [stream], offline == null ? [] : [offline]);

    private static ITranscriber NewStream(TranscriberId id, Language[] languages)
        => new StubTranscriber(new TranscriberInfo {
            Id = id,
            Kind = TranscriberKind.StreamOnly,
            Languages = new ApiSet<Language>(languages),
        });

    private static IOfflineTranscriber NewOffline(TranscriberId id, Language[] languages)
        => new StubOfflineTranscriber(new TranscriberInfo {
            Id = id,
            Kind = TranscriberKind.OfflineOnly,
            Languages = new ApiSet<Language>(languages),
        });

    // Nested types

    private sealed class StubTranscriber(TranscriberInfo info) : ITranscriber
    {
        public TranscriberInfo Info { get; } = info;
        public Task Transcribe(
            string audioStreamId,
            AudioSource audioSource,
            TranscriptionOptions options,
            ChannelWriter<Transcript> output,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubOfflineTranscriber(TranscriberInfo info) : IOfflineTranscriber
    {
        public TranscriberInfo Info { get; } = info;
        public Task<Transcript?> Transcribe(
            AudioSource audioSource,
            TranscriptionOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
