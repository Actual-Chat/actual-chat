namespace ActualChat.Audio;

[DataContract]
public sealed record AudioRecord(
    [property: DataMember] Symbol Id, // Ignored on upload
    [property: DataMember] Session Session,
    [property: DataMember] ChatId ChatId,
    [property: DataMember] double ClientStartOffset
    ) : IHasId<Symbol>
{
    private static Symbol NewId() => Ulid.NewUlid().ToString();

    public AudioRecord(Session session, ChatId chatId,  double clientStartOffset)
        : this(NewId(), session, chatId,  clientStartOffset) { }

    // This record relies on referential equality
    public bool Equals(AudioRecord? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
