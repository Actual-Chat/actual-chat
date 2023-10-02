using MemoryPack;
using Stl.Versioning;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Role(
    [property: DataMember, MemoryPackOrder(0)] RoleId Id, // Corresponds to DbRole.Id
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<RoleId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public string Picture { get; init; } = "";
    [DataMember, MemoryPackOrder(3)] public ChatPermissions Permissions { get; init; }
    [DataMember, MemoryPackOrder(4)] public string Name { get; init; } = "";
    [DataMember, MemoryPackOrder(5)] public SystemRole SystemRole { get; init; } = SystemRole.None;

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public long LocalId => Id.LocalId;

    private Role() : this(RoleId.None) { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public Role(string picture, ChatPermissions permissions, string name, SystemRole systemRole, RoleId id, long version = 0)
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
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record RoleDiff : RecordDiff
{
    [DataMember, MemoryPackOrder(0)] public string? Name { get; init; }
    [DataMember, MemoryPackOrder(1)] public SystemRole? SystemRole { get; init; }
    [DataMember, MemoryPackOrder(2)] public string? Picture { get; init; }
    [DataMember, MemoryPackOrder(3)] public ChatPermissions? Permissions { get; init; }
    [DataMember, MemoryPackOrder(4)] public SetDiff<ApiArray<AuthorId>, AuthorId> AuthorIds { get; init; }
}
