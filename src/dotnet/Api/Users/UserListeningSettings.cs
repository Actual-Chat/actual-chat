using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserListeningSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserListeningSettings);

    [DataMember, MemoryPackOrder(0), MemoryPackInclude]
    private ApiArray<ChatId> LegacyAlwaysListenedChatIds { get; init; }

    [IgnoreDataMember, MemoryPackIgnore]
    public IReadOnlyList<ChatId> AlwaysListenedChatIds {
        get => LegacyAlwaysListenedChatIds.Items;
        init => LegacyAlwaysListenedChatIds = ApiArray.New(value.ToArray());
    }

    [DataMember, MemoryPackOrder(1)] public string Origin { get; init; } = "";

    public UserListeningSettings WithAlwaysListeningChat(ChatId chatId)
        => this with { AlwaysListenedChatIds = AlwaysListenedChatIds.WithOrSkip(chatId) };

    public UserListeningSettings WithoutAlwaysListeningChat(ChatId chatId)
        => this with { AlwaysListenedChatIds = AlwaysListenedChatIds.Without(chatId) };
}
