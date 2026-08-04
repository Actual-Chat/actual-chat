using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// Defines a permission role within a chat.
/// </summary>
[DataContract, MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record Role(
    [property: DataMember, Key(0)] RoleId Id, // Corresponds to DbRole.Id
    [property: DataMember, Key(1)] long Version = 0
    ) : IHasId<RoleId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(2)] public string Picture { get; init; } = "";
    [DataMember, Key(3)] public ChatPermissions Permissions { get; init; }
    [DataMember, Key(4)] public string Name { get; init; } = "";
    [DataMember, Key(5)] public SystemRole SystemRole { get; init; } = SystemRole.None;

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public long LocalId => Id.LocalId;

    private Role() : this(null!) { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    public Role(RoleId id, long version, string picture, ChatPermissions permissions, string name, SystemRole systemRole)
        : this(id, version)
    {
        Picture = picture;
        Permissions = permissions;
        Name = name;
        SystemRole = systemRole;
    }

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

    // This record relies on referential equality
    public bool Equals(Role? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

/// <summary>
/// Represents changes to a <see cref="Role"/> for incremental updates.
/// </summary>
[DataContract, MessagePackObject(true)]
public sealed partial record RoleDiff : RecordDiff
{
    [DataMember] public string? Name { get; init; }
    [DataMember] public SystemRole? SystemRole { get; init; }
    [DataMember] public string? Picture { get; init; }
    [DataMember] public ChatPermissions? Permissions { get; init; }
    [DataMember] public SetDiff<AuthorId[], AuthorId> AuthorIds { get; init; }
}
