using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// Defines a permission role within a chat.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record Role(
    [property: DataMember, MemoryPackOrder(0), Key(0)] RoleId Id, // Corresponds to DbRole.Id
    [property: DataMember, MemoryPackOrder(1), Key(1)] long Version = 0
    ) : IHasId<RoleId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2), Key(2)] public string Picture { get; init; } = "";
    [DataMember, MemoryPackOrder(3), Key(3)] public ChatPermissions Permissions { get; init; }
    [DataMember, MemoryPackOrder(4), Key(4)] public string Name { get; init; } = "";
    [DataMember, MemoryPackOrder(5), Key(5)] public SystemRole SystemRole { get; init; } = SystemRole.None;

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public long LocalId => Id.LocalId;

    private Role() : this(null!) { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
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
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record RoleDiff : RecordDiff
{
    [DataMember, MemoryPackOrder(0)] public string? Name { get; init; }
    [DataMember, MemoryPackOrder(1)] public SystemRole? SystemRole { get; init; }
    [DataMember, MemoryPackOrder(2)] public string? Picture { get; init; }
    [DataMember, MemoryPackOrder(3)] public ChatPermissions? Permissions { get; init; }
    [DataMember, MemoryPackOrder(4)] public SetDiff<AuthorId[], AuthorId> AuthorIds { get; init; }
}
