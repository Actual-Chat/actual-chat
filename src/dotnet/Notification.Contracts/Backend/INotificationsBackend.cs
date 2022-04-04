namespace ActualChat.Notification.Backend;

public interface INotificationsBackend
{
    [ComputeMethod]
    Task<Device[]> GetDevices(string userId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<string[]> GetSubscribers(string chatId, CancellationToken cancellationToken);

//     [CommandHandler]
//     Task Create(CreateCommand command, CancellationToken cancellationToken);
//
//     public record CreateCommand(string AuthorId) : ICommand<Unit>, IBackendCommand;
}
