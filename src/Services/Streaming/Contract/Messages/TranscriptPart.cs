using MessagePack;

namespace ActualChat.Streaming
{
    [MessagePackObject]
    public record TranscriptPart(
        [property: Key(0)] string Text,
        [property: Key(1)] int TextOffset,
        [property: Key(2)] double StartOffset,
        [property: Key(3)] double Duration);
}
