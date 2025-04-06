using OpenSearch.Client;

namespace ActualChat.MLSearch.Documents;

// for partial updates
public interface IIndexedUserMinimalUpsert : IHasId<UserId>;

// for partial updates
public interface IIndexedUserUpsertForPlacesOnly : IHasId<UserId>
{
    ApiArray<PlaceId> PlaceIds { get; init; }
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
    public ApiArray<PlaceId> PlaceIds { get; init; } = ApiArray<PlaceId>.Empty;
    public JoinField ContactToUser { get; set; } = JoinField.Root<IndexedUser>();

    public static IndexedUser ForPartialPlacesUpsert(UserId userId, params ApiArray<PlaceId> placeIds)
        => new (userId) {
            PlaceIds = placeIds,
        };
}
