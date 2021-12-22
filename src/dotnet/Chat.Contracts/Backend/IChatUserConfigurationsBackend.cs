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
    Task Update(UpdateCommand command, CancellationToken cancellationToken);

    public record CreateCommand(string UserId, string ChatId) : ICommand<ChatUserConfiguration>, IBackendCommand { }

    public record UpdateCommand(string UserId, string ChatId, KeyValuePair<string, string> Option) : ICommand<Unit>, IBackendCommand;
}
