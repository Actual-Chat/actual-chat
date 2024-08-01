namespace ActualChat.Testing.Host;

public sealed record TestPlaceKey(
    int Index,
    bool IsPublic,
    bool MustJoin);