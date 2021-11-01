namespace ActualChat.Audio;

[DataContract]
public record AudioRecord(
        [property: DataMember(Order = 0)] AudioRecordId Id, // Ignored on upload
        [property: DataMember(Order = 1)] string AuthorId, // Ignored on upload
        [property: DataMember(Order = 2)] string ChatId,
        [property: DataMember(Order = 3)] AudioFormat Format,
        [property: DataMember(Order = 4)] string Language,
        [property: DataMember(Order = 5)] double ClientStartOffset)
    : IHasId<AudioRecordId>
{
    public AudioRecord() : this("", "", "", null!, "", 0) { }
    public AudioRecord(string chatId, AudioFormat format, string language, double clientStartOffset)
        : this("", "", chatId, format, language, clientStartOffset) { }
}
