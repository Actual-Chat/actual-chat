namespace ActualChat.Notification.Backend;

public interface INotificationsBackend
{
    [CommandHandler]
    public Task Create(CreateCommand command, CancellationToken cancellationToken);

    public record CreateCommand(string AuthorId) : ICommand<Unit>, IBackendCommand;
}
