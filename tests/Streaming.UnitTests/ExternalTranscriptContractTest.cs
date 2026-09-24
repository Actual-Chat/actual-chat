using ActualChat.Testing;
using ActualChat.Transcription;

namespace ActualChat.Streaming.UnitTests;

public class ExternalTranscriptContractTest
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
}
