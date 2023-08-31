using ActualChat.Users;
using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record AuthorRules(
    [property: DataMember, MemoryPackOrder(0)] Symbol ChatId,
    [property: DataMember, MemoryPackOrder(1)] AuthorFull? Author,
    [property: DataMember, MemoryPackOrder(2)] AccountFull Account,
    [property: DataMember, MemoryPackOrder(3)] ChatPermissions Permissions = default
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
    public bool CanManageMembers() => IsOwner();
    public bool CanEditProperties() => Permissions.Has(ChatPermissions.EditProperties);
    public bool CanEditRoles() => Permissions.Has(ChatPermissions.EditRoles);
    public bool IsOwner() => Permissions.Has(ChatPermissions.Owner);

    public bool Has(ChatPermissions required)
        => Permissions.Has(required);
    public void Require(ChatPermissions required)
        => Permissions.Require(required);
}
