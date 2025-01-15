namespace ActualChat.Testing.Host;

public record TestChatKey(
    TestPlaceKey? PlaceKey,
    int Index,
    bool MustJoin)
{
    public static TestChatKey Friend1(TestPlaceKey? placeKey)
        => new (placeKey, 0, true);
    public static TestChatKey Friend2(TestPlaceKey? placeKey)
        => new (placeKey, 1, true);
    public static TestChatKey Stranger1(TestPlaceKey? placeKey)
        => new (placeKey, 0, false);
    public static TestChatKey Stranger2(TestPlaceKey? placeKey)
        => new (placeKey, 1, false);
}
