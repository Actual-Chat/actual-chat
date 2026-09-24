using ActualChat.Chat;
using ActualChat.Testing;
using ActualChat.Transcription;

namespace ActualChat.Streaming.UnitTests;

public sealed class ExternalTranscriptContractTest
{
    [Fact]
    public void ChunkShouldRoundTripThroughEverySerializer()
    {
        // arrange
        var chunk = new ExternalTranscriptChunk("Hello", true, 1.25, true);

        // act
        var act = () => chunk.AssertPassesThroughSerializers();

        // assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ChunkShouldRoundTripWithoutAnOffset()
    {
        // arrange - a producer with no alignment data leaves AudioOffset null
        var chunk = new ExternalTranscriptChunk("Hello", true, null, false);

        // act
        var restored = chunk.PassThroughSerializers();

        // assert
        restored.AudioOffset.Should().BeNull();
    }

    [Fact]
    public void VoiceStreamShouldRoundTripThroughEverySerializer()
    {
        // arrange - before the entry exists, which is how every reply but the last one looks
        var stream = new ChatVoiceStream(
            StreamId.New(new NodeRef(Generate.Option)), null, 12, 4096, false, TimeSpan.FromSeconds(1.5));

        // act
        var restored = stream.PassThroughSerializers();

        // assert
        restored.EntryId.Should().BeNull();
        restored.AudioDuration.Should().Be(TimeSpan.FromSeconds(1.5));
    }
}
