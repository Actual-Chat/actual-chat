namespace ActualChat.Chat;

public interface IChatUserSettingsBackend
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ChatUserSettings?> Get(string userId, string chatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<ChatUserSettings> Upsert(UpsertCommand command, CancellationToken cancellationToken);

    public record UpsertCommand(string UserId, string ChatId, ChatUserSettings Settings)
        : ICommand<ChatUserSettings>, IBackendCommand { }
}
