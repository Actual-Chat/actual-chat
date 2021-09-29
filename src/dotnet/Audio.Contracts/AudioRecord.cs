using ActualChat.Audio;
using MessagePack;
using Stl;

namespace ActualChat.Audio
{
    [MessagePackObject]
    public record AudioRecord(
        [property: Key(0)] AudioRecordId Id, // Ignored on upload
        [property: Key(1)] string UserId, // Ignored on upload
        [property: Key(2)] string ChatId,
        [property: Key(3)] AudioFormat Format,
        [property: Key(4)] string Language,
        [property: Key(5)] double ClientStartOffset)
        : IHasId<AudioRecordId>
    {
        public AudioRecord() : this("", "", "", null!, "", 0) { }
        public AudioRecord(string chatId, AudioFormat format, string language, double clientStartOffset)
            : this("", "", chatId, format, language, clientStartOffset) { }
    }
}
