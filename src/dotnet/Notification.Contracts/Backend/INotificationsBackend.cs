namespace ActualChat.Notification.Backend;

public interface INotificationsBackend
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ImmutableArray<Device>> ListDevices(string userId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ImmutableArray<Symbol>> ListSubscriberIds(string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task NotifySubscribers(NotifySubscribersCommand subscribersCommand, CancellationToken cancellationToken);

    [DataContract]
    public sealed record NotifySubscribersCommand(
        [property: DataMember] string ChatId,
        [property: DataMember] long EntryId,
        [property: DataMember] string AuthorUserId,
        [property: DataMember] string Title,
        [property: DataMember] string Content
    ) : ICommand<Unit>, IBackendCommand;
}
