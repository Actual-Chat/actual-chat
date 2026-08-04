namespace ActualChat.Notifications;

/// <summary>
/// Service for managing user notifications and device registrations.
/// </summary>
public interface INotifications : IComputeService
{
    [ComputeMethod(MinCacheDuration = 30)]
    Task<ApiArray<Notification>> ListActive(Session session, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<bool> HasNotifiedMentionedMembers(
        Session session, ChatEntryId chatEntryId, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnHandle(Notifications_Handle command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnHandleAll(Notifications_HandleAll command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRegisterDevice(Notifications_RegisterDevice command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDeregisterDevice(Notifications_DeregisterDevice command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnNotifyMembers(Notifications_NotifyMembers command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnNotifyMentionedMembers(Notifications_NotifyMentionedMembers command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_Handle(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] NotificationId NotificationId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_HandleAll(
    [property: DataMember, Key(0)] Session Session
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_RegisterDevice(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] Symbol DeviceId,
    [property: DataMember, Key(2)] DeviceType DeviceType
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_DeregisterDevice(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] Symbol DeviceId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_NotifyMembers(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId ChatId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_NotifyMentionedMembers(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatEntryId ChatEntryId
) : ISessionCommand<Unit>, IApiCommand;
