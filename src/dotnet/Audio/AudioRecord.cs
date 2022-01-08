namespace ActualChat.Audio;

[DataContract]
public record AudioRecord(
        [property: DataMember(Order = 0)] string Id, // Ignored on upload
        [property: DataMember(Order = 1)] string SessionId,
        [property: DataMember(Order = 2)] string ChatId,
        [property: DataMember(Order = 3)] AudioFormat Format,
        [property: DataMember(Order = 4)] double ClientStartOffset)
    : IHasId<string>
{
    public AudioRecord() : this("", "", null!, null!, 0) { }
    public AudioRecord(string sessionId, string chatId, AudioFormat format, double clientStartOffset)
        : this("", sessionId, chatId, format, clientStartOffset) { }
}
