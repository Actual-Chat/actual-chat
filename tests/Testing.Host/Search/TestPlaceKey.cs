namespace ActualChat.Testing.Host;

public sealed record TestPlaceKey(
    int Index,
    bool IsPublic,
    bool MustJoin)
{
    public static readonly TestPlaceKey JoinedPublicPlace1 = new (0, true, true);
    public static readonly TestPlaceKey OtherPublicPlace1 = new (0, true, false);
    public static readonly TestPlaceKey JoinedPublicPlace2 = new (1, true, true);
    public static readonly TestPlaceKey OtherPublicPlace2 = new (1, true, false);
    public static readonly TestPlaceKey JoinedPrivatePlace1 = new (0, false, true);
    public static readonly TestPlaceKey JoinedPrivatePlace2 = new (1, false, true);
}
