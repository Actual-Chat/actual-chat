namespace ActualChat.Notification.Backend;

public interface INotificationsBackend : IComputeService
{
    [ComputeMethod]
    Task<ImmutableArray<Device>> ListDevices(string userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListSubscriberIds(string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task NotifySubscribers(NotifySubscribersCommand subscribersCommand, CancellationToken cancellationToken);

    [CommandHandler]
    Task RemoveDevices(RemoveDevicesCommand removeDevicesCommand, CancellationToken cancellationToken);

    [DataContract]
    public sealed record NotifySubscribersCommand(
        [property: DataMember] string ChatId,
        [property: DataMember] long EntryId,
        [property: DataMember] string AuthorUserId,
        [property: DataMember] string Title,
        [property: DataMember] string Content
    ) : ICommand<Unit>, IBackendCommand;

    [DataContract]
    public sealed record RemoveDevicesCommand(
        [property: DataMember] ImmutableArray<string> DeviceIds
    ) : ICommand<Unit>, IBackendCommand;
}
