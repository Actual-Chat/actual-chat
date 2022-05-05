namespace ActualChat.Notification.Backend;

public interface INotificationsBackend
{
    [ComputeMethod]
    Task<Device[]> GetDevices(string userId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<string[]> GetSubscribers(string chatId, CancellationToken cancellationToken);

     [CommandHandler]
     Task NotifySubscribers(NotifySubscribersCommand subscribersCommand, CancellationToken cancellationToken);

     [DataContract]
     public record NotifySubscribersCommand(
         [property: DataMember] string ChatId,
         [property: DataMember] long EntryId,
         [property: DataMember] string AuthorUserId,
         [property: DataMember] string Title,
         [property: DataMember] string Content
         ) : ICommand<Unit>, IBackendCommand;
}
