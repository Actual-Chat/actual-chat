using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface IChatsUpgradeBackend : ICommandService, IBackendService
{
    [CommandHandler]
    Task<Chat> OnCreateDefaultChat(ChatsUpgradeBackend_CreateDefaultChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Chat> OnCreateAnnouncementsChat(ChatsUpgradeBackend_CreateAnnouncementsChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Chat> OnCreateFeedbackTemplateChat(ChatsUpgradeBackend_CreateFeedbackTemplateChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Chat> OnCreateAiChat(ChatsUpgradeBackend_CreateAiChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnUpgradeChat(ChatsUpgradeBackend_UpgradeChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnFixCorruptedReadPositions(ChatsUpgradeBackend_FixCorruptedReadPositions command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_CreateDefaultChat(
) : ICommand<Chat>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => Constants.Chat.DefaultChatId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_CreateAnnouncementsChat(
) : ICommand<Chat>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => Constants.Chat.AnnouncementsChatId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_CreateFeedbackTemplateChat(
) : ICommand<Chat>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => Constants.Chat.FeedbackTemplateChatId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_CreateAiChat(
) : ICommand<Chat>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => Constants.Chat.AiChatId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_FixCorruptedReadPositions(
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => default;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_UpgradeChat(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;
}
