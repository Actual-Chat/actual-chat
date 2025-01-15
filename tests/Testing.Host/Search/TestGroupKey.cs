namespace ActualChat.Testing.Host;

public sealed record TestGroupKey(
    TestPlaceKey? PlaceKey,
    int Index,
    bool IsPublic,
    bool MustJoin) : TestChatKey(PlaceKey, Index, MustJoin)
{
    public bool NeedsExplicitJoin => MustJoin && (!IsPublic || PlaceKey == null);

    public static TestGroupKey JoinedPublicChat1(TestPlaceKey? placeKey) => new (placeKey, 0, true, true);
    public static TestGroupKey JoinedPublicChat2(TestPlaceKey? placeKey) => new (placeKey, 1, true, true);
    public static TestGroupKey OtherPublicChat1(TestPlaceKey? placeKey) => new (placeKey, 0, true, false);
    public static TestGroupKey OtherPublicChat2(TestPlaceKey? placeKey) => new (placeKey, 1, true, false);
    public static TestGroupKey JoinedPrivateChat1(TestPlaceKey? placeKey) => new (placeKey, 0, false, true);
    public static TestGroupKey JoinedPrivateChat2(TestPlaceKey? placeKey) => new (placeKey, 1, false, true);
}
