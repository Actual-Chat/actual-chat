using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for chat migration and upgrade operations.
/// </summary>
public interface IChatsUpgradeBackend : ICommandService, IBackendService
{
    [CommandHandler]
    Task<Chat> OnCreateDefaultChat(ChatsUpgradeBackend_CreateDefaultChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Chat> OnCreateAnnouncementsChat(ChatsUpgradeBackend_CreateAnnouncementsChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Chat> OnCreateFeedbackTemplateChat(ChatsUpgradeBackend_CreateFeedbackTemplateChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnUpgradeChat(ChatsUpgradeBackend_UpgradeChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnFixCorruptedReadPositions(ChatsUpgradeBackend_FixCorruptedReadPositions command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to create the default public chat.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_CreateDefaultChat(
) : ICommand<Chat>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => Constants.Chat.DefaultChatId;
}

/// <summary>
/// Command to create the system announcements chat.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_CreateAnnouncementsChat(
) : ICommand<Chat>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => Constants.Chat.AnnouncementsChatId;
}

/// <summary>
/// Command to create the feedback template chat.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_CreateFeedbackTemplateChat(
) : ICommand<Chat>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => Constants.Chat.FeedbackTemplateChatId;
}

/// <summary>
/// Command to fix corrupted read position records.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_FixCorruptedReadPositions(
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId?>
{
    [IgnoreDataMember, IgnoreMember]
    public ChatId? ShardKey => null;
}

/// <summary>
/// Command to upgrade a single chat to the latest schema.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsUpgradeBackend_UpgradeChat(
    [property: DataMember, Key(0)] ChatId ChatId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => ChatId;
}
