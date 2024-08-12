using ActualChat.Chat;

namespace ActualChat.Testing.Host;

public static class TestPlacesExt
{
    public static Place JoinedPublicPlace1(this IReadOnlyDictionary<TestPlaceKey, Place> places) => places[new (0, true, true)];
    public static Place JoinedPrivatePlace1(this IReadOnlyDictionary<TestPlaceKey, Place> places) => places[new (0, false, true)];
    public static Place JoinedPrivatePlace2(this IReadOnlyDictionary<TestPlaceKey, Place> places) => places[new (1, false, true)];
    public static Place OtherPublicPlace1(this IReadOnlyDictionary<TestPlaceKey, Place> places) => places[new (0, true, false)];

    public static IEnumerable<Place> Joined1(this IReadOnlyDictionary<TestPlaceKey, Place> places)
        => [places.JoinedPublicPlace1(), places.JoinedPrivatePlace1()];

    public static int Size(this IReadOnlyDictionary<TestPlaceKey, Place> places)
        => places.Keys.Max(x => x.Index) + 1;

    public static IEnumerable<Place> Joined(this IReadOnlyDictionary<TestPlaceKey, Place> places)
        => places.Where(x => x.Key.MustJoin).Select(x => x.Value);

    public static IEnumerable<Place> OtherPublic(this IReadOnlyDictionary<TestPlaceKey, Place> places)
        => places.Where(x => x.Key is { MustJoin: false, IsPublic: true }).Select(x => x.Value);
}
