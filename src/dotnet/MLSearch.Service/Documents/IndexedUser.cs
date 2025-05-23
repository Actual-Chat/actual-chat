using OpenSearch.Client;

namespace ActualChat.MLSearch.Documents;

// for partial updates
public interface IIndexedUserMinimalUpsert : IHasId<UserId>;

// for partial updates
public interface IIndexedUserUpsertForPlacesOnly : IHasId<UserId>
{
    PlaceId[] PlaceIds { get; init; }
}

// for partial updates
public interface IIndexedUserUpsertWithoutPlaces : IHasId<UserId>
{
    string Name { get; init; }
}

public sealed record IndexedUser(UserId Id) : IIndexedUserUpsertWithoutPlaces,
    IIndexedUserUpsertForPlacesOnly,
    IIndexedUserMinimalUpsert,
    IHasRoutingKey<UserId>,
    IRequirementTarget
{
    public string Name { get; init; } = "";
    // ReSharper disable once TypeWithSuspiciousEqualityIsUsedInRecord.Global
    public PlaceId[] PlaceIds { get; init; } = [];
    public JoinField ContactToUser => JoinField.Root<IndexedUser>();

    public static IndexedUser ForPartialPlacesUpsert(UserId userId, params PlaceId[] placeIds)
        => new (userId) { PlaceIds = placeIds };
}
