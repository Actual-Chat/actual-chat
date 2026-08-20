namespace ActualChat.Invite;

/// <summary>
/// Invite link for joining a specific chat.
/// </summary>
[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ChatInvite(Symbol Id, long Version = 0) : Invite(Id, Version)
{
    [DataMember, Key(10)] public ChatId ChatId { get; init; } = null!;
    public static ChatInvite New(int remaining, ChatId chatId)
        => new(Symbol.Empty) { Remaining = remaining, ChatId = chatId };
    public ChatInvite() : this(Symbol.Empty) { }

    // Keep the v2.7 string ("ChatInviteOption:...") so existing DbInvite.SearchKey
    // rows stay reachable across the union refactor.
    public static string GetSearchKey(ChatId chatId)
        => $"ChatInviteOption:{chatId}";

    public override string GetSearchKey()
        => GetSearchKey(ChatId);

    public override bool Grants(ChatId chatId)
        => ChatId == chatId
            || (ChatId is PlaceChatId { IsRoot: false } placeChatId && placeChatId.RootChatId == chatId);
}
