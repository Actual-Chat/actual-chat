using Stl.Versioning;

namespace ActualChat.Chat;

[DataContract]
public sealed record ChatRole(
    [property: DataMember] Symbol Id, // Corresponds to DbChatRole.Id
    [property: DataMember] string Name = "",
    [property: DataMember] SystemChatRole SystemRole = SystemChatRole.None
    ) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember] public long Version { get; init; } = 0;
    [DataMember] public string Picture { get; init; } = "";
    [DataMember] public ChatPermissions Permissions { get; init; }

    public ChatRole Fix()
    {
        var role = this;
        if (role.SystemRole is SystemChatRole.Owner && !role.Permissions.Has(ChatPermissions.Owner))
            role = role with { Permissions = ChatPermissions.Owner.AddImplied() };
        if (role.SystemRole is not SystemChatRole.None) {
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
public sealed record ChatRoleDiff : RecordDiff
{
    [DataMember] public string? Name { get; init; }
    [DataMember] public SystemChatRole? SystemRole { get; init; }
    [DataMember] public string? Picture { get; init; }
    [DataMember] public ChatPermissions? Permissions { get; init; }
    [DataMember] public SetDiff<ImmutableArray<Symbol>, Symbol> AuthorIds { get; init; } =
        SetDiff<ImmutableArray<Symbol>, Symbol>.Unchanged;
}
