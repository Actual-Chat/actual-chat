namespace ActualChat.Notification.Backend;

public interface INotificationsBackend
{
    [ComputeMethod]
    Task<Device[]> GetDevices(string userId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<string[]> GetSubscribers(string chatId, CancellationToken cancellationToken);

     [CommandHandler]
     Task NotifySubscribers(NotifySubscribersCommand subscribersCommand, CancellationToken cancellationToken);

     public record NotifySubscribersCommand(
         string ChatId,
         long EntryId,
         string AuthorUserId,
         string Title,
         string Content) : ICommand<Unit>, IBackendCommand;
}
