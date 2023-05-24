namespace ActualChat.Chat;

public interface IChatsUpgradeBackend : ICommandService
{
    [CommandHandler]
    Task<Chat> CreateAnnouncementsChat(CreateAnnouncementsChatCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Chat> CreateDefaultChat(CreateDefaultChatCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Chat> CreateFeedbackTemplateChat(CreateFeedbackTemplateChatCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task UpgradeChat(UpgradeChatCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task FixCorruptedReadPositions(FixCorruptedReadPositionsCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record CreateAnnouncementsChatCommand(
    ) : ICommand<Chat>, IBackendCommand;

    [DataContract]
    public sealed record CreateDefaultChatCommand(
    ) : ICommand<Chat>, IBackendCommand;

    [DataContract]
    public sealed record CreateFeedbackTemplateChatCommand(
    ) : ICommand<Chat>, IBackendCommand;


    public sealed record UpgradeChatCommand(
        [property: DataMember] ChatId ChatId
    ) : ICommand<Unit>, IBackendCommand;

    [DataContract]
    public sealed record FixCorruptedReadPositionsCommand(
    ) : ICommand<Unit>, IBackendCommand;
}
