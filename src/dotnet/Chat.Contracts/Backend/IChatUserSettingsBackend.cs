namespace ActualChat.Chat;

public interface IChatUserSettingsBackend
{
    Task<ChatUserSettings> GetOrCreate(string userId, string chatId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ChatUserSettings?> Get(string userId, string chatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<ChatUserSettings> Create(CreateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task SetLanguage(SetLanguageCommand command, CancellationToken cancellationToken);

    public record CreateCommand(string UserId, string ChatId) : ICommand<ChatUserSettings>, IBackendCommand { }
    public record SetLanguageCommand(string UserId, string ChatId, LanguageId Language) : ICommand<Unit>, IBackendCommand;
}
