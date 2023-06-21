using ActualChat.Users;

namespace ActualChat.Chat;

[DataContract]
public sealed record AuthorRules(
    [property: DataMember] Symbol ChatId,
    [property: DataMember] AuthorFull? Author,
    [property: DataMember] AccountFull Account,
    [property: DataMember] ChatPermissions Permissions = default
    ) : IRequirementTarget
{
    public static Requirement<AuthorRules> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<AuthorRules>()),
        (AuthorRules? a) => a is { ChatId.IsEmpty: false });

    public static AuthorRules None(ChatId chatId) => new(chatId, AuthorFull.None, AccountFull.None);

    public bool CanRead() => Permissions.Has(ChatPermissions.Read);
    public bool CanWrite() => Permissions.Has(ChatPermissions.Write);
    public bool CanSeeMembers() => Permissions.Has(ChatPermissions.SeeMembers);
    public bool CanJoin() => Permissions.Has(ChatPermissions.Join);
    public bool CanLeave() => Permissions.Has(ChatPermissions.Leave);
    public bool CanInvite() => Permissions.Has(ChatPermissions.Invite);
    public bool CanEditProperties() => Permissions.Has(ChatPermissions.EditProperties);
    public bool CanEditRoles() => Permissions.Has(ChatPermissions.EditRoles);
    public bool IsOwner() => Permissions.Has(ChatPermissions.Owner);

    public bool Has(ChatPermissions required)
        => Permissions.Has(required);
    public void Require(ChatPermissions required)
        => Permissions.Require(required);


}
