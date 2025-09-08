using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserListeningSettings : IHasOrigin, IHasKvasKey<UserListeningSettings>
{
    [DataMember, MemoryPackOrder(0)]
    public ChatId[] AlwaysListenedChatIds { get; init; } = [];

    [DataMember, MemoryPackOrder(1)]
    public string Origin { get; init; } = "";

    public UserListeningSettings WithAlwaysListeningChat(ChatId chatId)
        => this with { AlwaysListenedChatIds = AlwaysListenedChatIds.WithOrSkip(chatId).ToArray() };

    public UserListeningSettings WithoutAlwaysListeningChat(ChatId chatId)
        => this with { AlwaysListenedChatIds = AlwaysListenedChatIds.Without(chatId).ToArray() };
}
