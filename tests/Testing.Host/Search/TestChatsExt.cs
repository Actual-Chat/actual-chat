namespace ActualChat.Testing.Host;

public static class TestChatsExt
{
    // TODO: generate code
    public static Chat.Chat JoinedPublicChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (null, 0, true, true)];
    public static Chat.Chat JoinedPublicChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (null, 1, true, true)];
    public static Chat.Chat OtherPublicChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (null, 0, true, false)];
    public static Chat.Chat OtherPublicChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (null, 1, true, false)];
    public static Chat.Chat JoinedPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (null, 0, false, true)];
    public static Chat.Chat JoinedPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (null, 1, false, true)];
    public static Chat.Chat OtherPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (null, 0, false, false)];
    public static Chat.Chat OtherPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (null, 1, false, false)];

    public static Chat.Chat JoinedPublicPlace1JoinedPublicChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, true, true), 0, true, true)];
    public static Chat.Chat JoinedPublicPlace1JoinedPublicChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, true, true), 1, true, true)];
    public static Chat.Chat JoinedPublicPlace2JoinedPublicChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, true, true), 0, true, true)];
    public static Chat.Chat JoinedPublicPlace2JoinedPublicChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, true, true), 1, true, true)];

    public static Chat.Chat JoinedPublicPlace1JoinedPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, true, true), 0, false, true)];
    public static Chat.Chat JoinedPublicPlace1JoinedPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, true, true), 1, false, true)];
    public static Chat.Chat JoinedPublicPlace2JoinedPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, true, true), 0, false, true)];
    public static Chat.Chat JoinedPublicPlace2JoinedPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, true, true), 1, false, true)];

    public static Chat.Chat JoinedPublicPlace1OtherPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, true, true), 0, false, false)];
    public static Chat.Chat JoinedPublicPlace1OtherPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, true, true), 1, false, false)];
    public static Chat.Chat JoinedPublicPlace2OtherPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, true, true), 0, false, false)];
    public static Chat.Chat JoinedPublicPlace2OtherPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, true, true), 1, false, false)];

    public static Chat.Chat OtherPublicPlace1JoinedPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, true, false), 0, false, true)];
    public static Chat.Chat OtherPublicPlace1JoinedPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, true, false), 1, false, true)];
    public static Chat.Chat OtherPublicPlace2JoinedPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, true, false), 0, false, true)];
    public static Chat.Chat OtherPublicPlace2JoinedPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, true, false), 1, false, true)];

    public static Chat.Chat JoinedPrivatePlace1JoinedPublicChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, false, true), 0, true, true)];
    public static Chat.Chat JoinedPrivatePlace1JoinedPublicChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, false, true), 1, true, true)];
    public static Chat.Chat JoinedPrivatePlace2JoinedPublicChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, false, true), 0, true, true)];
    public static Chat.Chat JoinedPrivatePlace2JoinedPublicChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, false, true), 1, true, true)];

    public static Chat.Chat JoinedPrivatePlace1JoinedPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, false, true), 0, false, true)];
    public static Chat.Chat JoinedPrivatePlace1JoinedPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, false, true), 1, false, true)];
    public static Chat.Chat JoinedPrivatePlace2JoinedPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, false, true), 0, false, true)];
    public static Chat.Chat JoinedPrivatePlace2JoinedPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, false, true), 1, false, true)];

    public static Chat.Chat JoinedPrivatePlace1OtherPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, false, true), 0, false, false)];
    public static Chat.Chat JoinedPrivatePlace1OtherPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, false, true), 1, false, false)];
    public static Chat.Chat JoinedPrivatePlace2OtherPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, false, true), 0, false, false)];
    public static Chat.Chat JoinedPrivatePlace2OtherPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, false, true), 1, false, false)];

    public static Chat.Chat OtherPublicPlace1OtherPublicChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, true, false), 0, true, false)];
    public static Chat.Chat OtherPublicPlace1OtherPublicChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, true, false), 1, true, false)];
    public static Chat.Chat OtherPublicPlace2OtherPublicChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, true, false), 0, true, false)];
    public static Chat.Chat OtherPublicPlace2OtherPublicChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, true, false), 1, true, false)];

    public static Chat.Chat OtherPublicPlace1OtherPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, true, false), 0, false, false)];
    public static Chat.Chat OtherPublicPlace1OtherPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, true, false), 1, false, false)];
    public static Chat.Chat OtherPublicPlace2OtherPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, true, false), 0, false, false)];
    public static Chat.Chat OtherPublicPlace2OtherPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, true, false), 1, false, false)];

    public static Chat.Chat OtherPrivatePlace1OtherPublicChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, false, false), 0, true, false)];
    public static Chat.Chat OtherPrivatePlace1OtherPublicChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, false, false), 1, true, false)];
    public static Chat.Chat OtherPrivatePlace2OtherPublicChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, false, false), 0, true, false)];
    public static Chat.Chat OtherPrivatePlace2OtherPublicChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, false, false), 1, true, false)];

    public static Chat.Chat OtherPrivatePlace1OtherPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, false, false), 0, false, false)];
    public static Chat.Chat OtherPrivatePlace1OtherPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(0, false, false), 1, false, false)];
    public static Chat.Chat OtherPrivatePlace2OtherPrivateChat1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, false, false), 0, false, false)];
    public static Chat.Chat OtherPrivatePlace2OtherPrivateChat2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats[new (new(1, false, false), 1, false, false)];

    public static IEnumerable<Chat.Chat> JoinedGroups1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats) => [
        chats.JoinedPublicChat1(),
        chats.JoinedPrivateChat1(),
        chats.JoinedPublicPlace1JoinedPublicChat1(),
        chats.JoinedPublicPlace1JoinedPrivateChat1(),
        chats.JoinedPrivatePlace1JoinedPublicChat1(),
        chats.JoinedPrivatePlace1JoinedPrivateChat1(),
    ];

    public static IEnumerable<Chat.Chat> JoinedGroups2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats) => chats.JoinedGroups1()
        .Concat([
            chats.JoinedPublicChat2(),
            chats.JoinedPrivateChat2(),
            chats.JoinedPublicPlace1JoinedPublicChat2(),
            chats.JoinedPublicPlace1JoinedPrivateChat2(),
            chats.JoinedPrivatePlace1JoinedPublicChat2(),
            chats.JoinedPrivatePlace1JoinedPrivateChat2(),
            chats.JoinedPublicPlace2JoinedPublicChat1(),
            chats.JoinedPublicPlace2JoinedPublicChat2(),
            chats.JoinedPublicPlace2JoinedPrivateChat1(),
            chats.JoinedPublicPlace2JoinedPrivateChat2(),
            chats.JoinedPrivatePlace2JoinedPublicChat1(),
            chats.JoinedPrivatePlace2JoinedPublicChat2(),
            chats.JoinedPrivatePlace2JoinedPrivateChat1(),
            chats.JoinedPrivatePlace2JoinedPrivateChat2(),
        ]);

    public static IEnumerable<Chat.Chat> JoinedPrivatePlace1JoinedChats(
        this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats.Where(x => x.Key is { PlaceKey: { Index: 0, IsPublic: false, MustJoin: true }, MustJoin: true })
            .Select(x => x.Value);

    public static IEnumerable<Chat.Chat> JoinedPublicPlace1JoinedChats(
        this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats.Where(x => x.Key is { PlaceKey: { Index: 0, IsPublic: true, MustJoin: true }, MustJoin: true })
            .Select(x => x.Value);

    public static IEnumerable<Chat.Chat> OtherPublicGroups1(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats) => [
        chats.OtherPublicChat1(),
        chats.OtherPublicPlace1OtherPublicChat1(),
    ];

    public static IEnumerable<Chat.Chat> OtherPublicGroups2(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats) => chats.OtherPublicGroups1()
        .Concat([
            chats.OtherPublicChat2(),
            chats.OtherPublicPlace1OtherPublicChat2(),
            chats.OtherPublicPlace2OtherPublicChat1(),
            chats.OtherPublicPlace2OtherPublicChat2(),
        ]);

    public static IEnumerable<Chat.Chat> Joined(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats.Where(x => x.Key.MustJoin).Select(x => x.Value);

    public static IEnumerable<Chat.Chat> OtherPublic(this IReadOnlyDictionary<TestChatKey, Chat.Chat> chats)
        => chats.Where(x => x.Key.IsPublic && x.Key.PlaceKey?.IsPublic != false && !x.Key.MustJoin).Select(x => x.Value);
}
