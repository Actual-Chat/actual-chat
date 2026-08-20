namespace ActualChat.Invite;

/// <summary>
/// Invite link for adding the inviter as a contact (no chat / place attached).
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserInvite : Invite
{
    // Keep the v2.7 string ("UserInviteOption") so existing DbInvite.SearchKey
    // rows stay reachable across the union refactor.
    public static readonly string SearchKey = "UserInviteOption";
    public static UserInvite New(int remaining)
        => new(Symbol.Empty) { Remaining = remaining };
    public UserInvite() : base(Symbol.Empty) { }

    [SerializationConstructor]
    public UserInvite(Symbol id, long version = 0) : base(id, version) { }

    public override string GetSearchKey()
        => SearchKey;

    public override bool Grants(ChatId chatId)
        => false;
}
