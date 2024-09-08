namespace ActualChat.Testing.Host;

public record TestChatKey(
    TestPlaceKey? PlaceKey,
    int Index,
    bool MustJoin);
