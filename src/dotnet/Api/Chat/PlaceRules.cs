using ActualChat.Users;

namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed partial record PlaceRules(
    [property: DataMember, Key(0)] PlaceId PlaceId,
    [property: DataMember, Key(1)] AuthorFull? Author,
    [property: DataMember, Key(2)] AccountFull? Account,
    [property: DataMember, Key(3)] PlacePermissions Permissions = default
    ) : IRequirementTarget
{
    public static PlaceRules None(PlaceId placeId) => new(placeId, null, null);

    public bool CanRead() => Permissions.Has(PlacePermissions.Read);
    public bool CanWrite() => Permissions.Has(PlacePermissions.Write);
    public bool CanSeeMembers() => Permissions.Has(PlacePermissions.SeeMembers);
    public bool CanJoin() => Permissions.Has(PlacePermissions.Join);
    public bool CanLeave() => Permissions.Has(PlacePermissions.Leave);
    public bool CanInvite() => Permissions.Has(PlacePermissions.Invite);
    public bool CanEditProperties() => Permissions.Has(PlacePermissions.EditProperties);
    public bool CanEditRoles() => Permissions.Has(PlacePermissions.EditRoles);
    public bool CanEditMembers() => Permissions.Has(PlacePermissions.EditMembers);
    public bool CanModerate() => Permissions.Has(PlacePermissions.Moderate);
    public bool IsOwner() => Permissions.Has(PlacePermissions.Owner);
    public bool CanApplyPublicChatType() => IsOwner();

    public bool Has(PlacePermissions required)
        => Permissions.Has(required);
    public void Require(PlacePermissions required)
        => Permissions.Require(required);

    // This record relies on referential equality
    public bool Equals(PlaceRules? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
