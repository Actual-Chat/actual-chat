namespace ActualChat.Testing.Host;

public sealed record TestGroupKey(
    TestPlaceKey? PlaceKey,
    int Index,
    bool IsPublic,
    bool MustJoin)
{
    public bool NeedsExplicitJoin => MustJoin && (!IsPublic || PlaceKey == null);
}
