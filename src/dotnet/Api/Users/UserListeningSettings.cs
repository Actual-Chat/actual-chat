using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserListeningSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserListeningSettings);

    [DataMember, MemoryPackOrder(0), MemoryPackInclude]
    private ApiArray<LegacyId> LegacyPinnedChats {
        get => AlwaysListenedChatIds.Select(x => LegacyId.Parse(x.Value)).ToApiArray();
        init => AlwaysListenedChatIds = value.Select(x => ChatId.Parse(x.Value)).ToArray()!;
    }

    [DataMember, MemoryPackOrder(1)]
    public string Origin { get; init; } = "";

    [IgnoreDataMember, MemoryPackIgnore]
    public IReadOnlyList<ChatId> AlwaysListenedChatIds { get; init; } = [];

    public UserListeningSettings WithAlwaysListeningChat(ChatId chatId)
        => this with { AlwaysListenedChatIds = AlwaysListenedChatIds.WithOrSkip(chatId) };

    public UserListeningSettings WithoutAlwaysListeningChat(ChatId chatId)
        => this with { AlwaysListenedChatIds = AlwaysListenedChatIds.Without(chatId) };
}
