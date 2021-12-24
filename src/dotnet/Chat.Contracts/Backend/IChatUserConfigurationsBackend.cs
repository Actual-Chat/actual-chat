namespace ActualChat.Chat;

public interface IChatUserConfigurationsBackend
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ChatUserConfiguration?> Get(string userId, string chatId, CancellationToken cancellationToken);

    Task<ChatUserConfiguration> GetOrCreate(string userId, string chatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<ChatUserConfiguration> Create(CreateCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task SetLanguage(SetLanguageCommand command, CancellationToken cancellationToken);

    public record CreateCommand(string UserId, string ChatId) : ICommand<ChatUserConfiguration>, IBackendCommand { }

    public record SetLanguageCommand(string UserId, string ChatId, string Language) : ICommand<Unit>, IBackendCommand;
}
