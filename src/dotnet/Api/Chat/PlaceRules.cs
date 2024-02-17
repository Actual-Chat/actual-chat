using ActualChat.Users;
using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record PlaceRules(
    [property: DataMember, MemoryPackOrder(0)] Symbol PlaceId,
    [property: DataMember, MemoryPackOrder(1)] AuthorFull? Author,
    [property: DataMember, MemoryPackOrder(2)] AccountFull Account,
    [property: DataMember, MemoryPackOrder(3)] PlacePermissions Permissions = default
) : IRequirementTarget
{
    public static PlaceRules None(PlaceId placeId) => new(placeId, AuthorFull.None, AccountFull.None);

    public bool CanRead() => Permissions.Has(PlacePermissions.Read);
    public bool CanWrite() => Permissions.Has(PlacePermissions.Write);
    public bool CanSeeMembers() => Permissions.Has(PlacePermissions.SeeMembers);
    public bool CanJoin() => Permissions.Has(PlacePermissions.Join);
    public bool CanLeave() => Permissions.Has(PlacePermissions.Leave);
    public bool CanInvite() => Permissions.Has(PlacePermissions.Invite);
    public bool CanEditProperties() => Permissions.Has(PlacePermissions.EditProperties);
    public bool CanEditRoles() => Permissions.Has(PlacePermissions.EditRoles);
    public bool CanEditMembers() => Permissions.Has(PlacePermissions.EditMembers);
    public bool IsOwner() => Permissions.Has(PlacePermissions.Owner);
    public bool CanApplyPublicChatType() => IsOwner();

    public bool Has(PlacePermissions required)
        => Permissions.Has(required);
    public void Require(PlacePermissions required)
        => Permissions.Require(required);
}
