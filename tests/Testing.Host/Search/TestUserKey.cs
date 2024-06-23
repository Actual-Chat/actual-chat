namespace ActualChat.Testing.Host;

public sealed record TestUserKey(
    TestPlaceKey? PlaceKey,
    int Index,
    bool IsExistingContact);
