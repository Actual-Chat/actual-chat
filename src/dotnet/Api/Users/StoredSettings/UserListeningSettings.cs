using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// User preferences for which chats to always listen to.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record UserListeningSettings : StoredSettings, IHasOrigin, IHasKvasKey<UserListeningSettings>
{
    [DataMember, MemoryPackOrder(0), Key(0)]
    public ChatId[] AlwaysListenedChatIds { get; init; } = [];

    [DataMember, MemoryPackOrder(1), Key(1)]
    public string Origin { get; init; } = "";

    public UserListeningSettings WithAlwaysListeningChat(ChatId chatId)
        => this with { AlwaysListenedChatIds = AlwaysListenedChatIds.WithOrSkip(chatId).ToArray() };

    public UserListeningSettings WithoutAlwaysListeningChat(ChatId chatId)
        => this with { AlwaysListenedChatIds = AlwaysListenedChatIds.Without(chatId).ToArray() };
}
