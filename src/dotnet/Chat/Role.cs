using Stl.Versioning;

namespace ActualChat.Chat;

[DataContract]
public sealed record Role(
    [property: DataMember] RoleId Id, // Corresponds to DbRole.Id
    [property: DataMember] string Name = "",
    [property: DataMember] SystemRole SystemRole = SystemRole.None
    ) : IHasId<RoleId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember] public long Version { get; init; } = 0;
    [DataMember] public string Picture { get; init; } = "";
    [DataMember] public ChatPermissions Permissions { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public long LocalId => Id.LocalId;

    public Role Fix()
    {
        var role = this;
        if (role.SystemRole is SystemRole.Owner && !role.Permissions.Has(ChatPermissions.Owner))
            role = role with { Permissions = ChatPermissions.Owner.AddImplied() };
        if (role.SystemRole is not SystemRole.None) {
            var name = role.SystemRole.ToString();
            if (!Equals(role.Name, name))
                role = role with { Name = name };
        }
        var permissions = role.Permissions.AddImplied();
        if (role.Permissions != permissions)
            role = role with { Permissions = permissions };
        return role;
    }
}

[DataContract]
public sealed record RoleDiff : RecordDiff
{
    [DataMember] public string? Name { get; init; }
    [DataMember] public SystemRole? SystemRole { get; init; }
    [DataMember] public string? Picture { get; init; }
    [DataMember] public ChatPermissions? Permissions { get; init; }
    [DataMember] public SetDiff<ImmutableArray<AuthorId>, AuthorId> AuthorIds { get; init; } =
        SetDiff<ImmutableArray<AuthorId>, AuthorId>.Unchanged;
}
