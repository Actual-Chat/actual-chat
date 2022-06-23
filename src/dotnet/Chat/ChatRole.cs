using Stl.Versioning;

namespace ActualChat.Chat;

[DataContract]
public sealed record ChatRole(
    [property: DataMember] Symbol Id, // Corresponds to DbChatRole.Id
    [property: DataMember] string Name = ""
    ) : IHasId<Symbol>, IHasVersion<long>
{
    public static ChatRole Everyone { get; } = new(":-1", "Everyone");
    public static ChatRole Users { get; } = new(":-2", "Users");
    public static ChatRole UnauthenticatedUsers { get; } = new(":-3", "Unauthenticated Users");
    public static ChatRole Owners { get; } = new(":-10", "Owners");

    public static IReadOnlyDictionary<Symbol, ChatRole> SystemRoles { get; } =
        new[] { Owners, Users, UnauthenticatedUsers, Everyone }.ToDictionary(r => r.Id);

    [DataMember] public long Version { get; init; } = 0;
    [DataMember] public string Picture { get; set; } = "";
    [DataMember] public ImmutableHashSet<Symbol> PrincipalIds { get; init; } = ImmutableHashSet<Symbol>.Empty;
}
