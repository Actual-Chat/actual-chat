
namespace ActualChat.Testing.Host;

public static class TestPlacesExt
{
    public static Place JoinedPublicPlace1(this IReadOnlyDictionary<TestPlaceKey, Place> places) => places[TestPlaceKey.JoinedPublicPlace1];
    public static Place JoinedPrivatePlace1(this IReadOnlyDictionary<TestPlaceKey, Place> places) => places[TestPlaceKey.JoinedPrivatePlace1];
    public static Place JoinedPrivatePlace2(this IReadOnlyDictionary<TestPlaceKey, Place> places) => places[TestPlaceKey.JoinedPrivatePlace2];
    public static Place OtherPublicPlace1(this IReadOnlyDictionary<TestPlaceKey, Place> places) => places[TestPlaceKey.OtherPublicPlace1];

    public static List<Place> Joined1(
        this IReadOnlyDictionary<TestPlaceKey, Place> places,
        string? filter = null)
        => new[] { places.JoinedPublicPlace1(), places.JoinedPrivatePlace1() }.Where(x
                => filter.IsNullOrEmpty() || x.Title.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public static List<Place> Joined(this IReadOnlyDictionary<TestPlaceKey, Place> places, string? filter = null)
        => places.Where(x => x.Key.MustJoin)
            .Where(x => filter.IsNullOrEmpty() || x.Value.Title.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToList();

    public static List<Place> OtherPublic(
        this IReadOnlyDictionary<TestPlaceKey, Place> places,
        string? filter = null)
        => places.Where(x => x.Key is { MustJoin: false, IsPublic: true })
            .Where(x => filter.IsNullOrEmpty() || x.Value.Title.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToList();

    public static int Size(this IReadOnlyDictionary<TestPlaceKey, Place> places)
        => places.Keys.Max(x => x.Index) + 1;
}
