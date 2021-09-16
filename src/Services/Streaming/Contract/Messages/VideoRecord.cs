using MessagePack;

namespace ActualChat.Streaming
{
    [MessagePackObject]
    public record VideoRecord(
        [property: Key(0)] VideoRecordId Id, 
        [property: Key(1)] string UserId,
        [property: Key(2)] string ChatId,
        [property: Key(3)] double ClientStartOffset);
}
