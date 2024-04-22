using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserListeningSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserListeningSettings);

    [DataMember, MemoryPackOrder(0)] public IReadOnlyList<ChatId> AlwaysListenedChatIds { get; init; } = [];
    [DataMember, MemoryPackOrder(1)] public string Origin { get; init; } = "";

    public UserListeningSettings Add(ChatId chatId)
    {
        if (AlwaysListenedChatIds.Contains(chatId))
            return this;

        var skipCount = AlwaysListenedChatIds.Count >= 3
            ? 1
            : 0;
        var listenedChats = new List<ChatId>(AlwaysListenedChatIds.Skip(skipCount)) { chatId };
        return this with { AlwaysListenedChatIds = listenedChats };
    }

    public UserListeningSettings Remove(ChatId chatId)
    {
        var listenedChats = new List<ChatId>(AlwaysListenedChatIds.Where(cid => cid != chatId));
        return this with { AlwaysListenedChatIds = listenedChats };
    }
}

