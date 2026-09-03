namespace ActualChat.Notifications;

/// <summary>
/// Base for notifications related to a chat. The similarity key is the <see cref="ChatId"/>.
/// </summary>
[DataContract]
public abstract partial record ChatNotification(NotificationId Id, long Version = 0)
    : Notification(Id, Version)
{
    [DataMember(Order = 8), Key(8)]
    public AuthorId? AuthorId { get; init; }
    // Rows and peers older than these keys deserialize them as null (absent array-form slots
    // overwrite the initializer), so the setter coalesces.
    [DataMember(Order = 21), Key(21)]
    public string SenderName {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init => field = value ?? "";
    } = "";
    [DataMember(Order = 22), Key(22)]
    public string GroupTitle {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init => field = value ?? "";
    } = "";

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public virtual ChatId ChatId => ChatId.Parse(SimilarityKey);
}
