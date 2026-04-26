namespace ActualChat.Invite;

/// <summary>
/// Invite link for adding the inviter as a contact (no chat / place attached).
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserInvite : Invite
{
    public UserInvite() : base(Symbol.Empty) { }

    [SerializationConstructor]
    public UserInvite(Symbol id, long version = 0) : base(id, version) { }

    public static UserInvite New(int remaining)
        => new(Symbol.Empty) { Remaining = remaining };

    public static readonly string SearchKey = nameof(UserInvite);

    public override string GetSearchKey()
        => SearchKey;
}
