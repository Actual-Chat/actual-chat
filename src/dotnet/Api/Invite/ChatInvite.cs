namespace ActualChat.Invite;

/// <summary>
/// Invite link for joining a specific chat.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ChatInvite : Invite
{
    [DataMember, Key(10)] public ChatId ChatId { get; init; }

    public ChatInvite() : base(Symbol.Empty) { }

    [SerializationConstructor]
    public ChatInvite(Symbol id, long version = 0) : base(id, version) { }

    public static ChatInvite New(int remaining, ChatId chatId)
        => new(Symbol.Empty) { Remaining = remaining, ChatId = chatId };

    public static string GetSearchKey(ChatId chatId)
        => $"{nameof(ChatInvite)}:{chatId}";

    public override string GetSearchKey()
        => GetSearchKey(ChatId);
}
