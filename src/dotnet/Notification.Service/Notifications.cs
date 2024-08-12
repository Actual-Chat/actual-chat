using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.Notification;

public class Notifications(IServiceProvider services) : INotifications
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private INotificationsBackend Backend { get; } = services.GetRequiredService<INotificationsBackend>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private ILogger Log { get; } = services.LogFor<Notifications>();

    private MomentClockSet Clocks { get; } = services.Clocks();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<Notification?> Get(
        Session session, NotificationId notificationId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (notificationId.UserId != account.Id)
            throw Unauthorized();

        return await Backend.Get(notificationId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<IReadOnlyList<NotificationId>> ListRecentNotificationIds(
        Session session, Moment minSentAt, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return await Backend.ListRecentNotificationIds(account.Id, minSentAt, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnHandle(
        Notifications_Handle command, CancellationToken cancellationToken)
    {
        var (session, notificationId) = command;
        var notification = await Get(session, notificationId, cancellationToken).Require().ConfigureAwait(false);
        if (notification.HandledAt.HasValue)
            return;

        notification = notification with {
            HandledAt = Clocks.SystemClock.Now,
        };
        var upsertCommand = new NotificationsBackend_Upsert(notification);
        await Commander.Run(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRegisterDevice(
        Notifications_RegisterDevice command, CancellationToken cancellationToken)
    {
        var (session, deviceId, deviceType) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var registerDeviceCommand = new NotificationsBackend_RegisterDevice(account.Id, deviceId, deviceType, session.Hash);
        await Commander.Run(registerDeviceCommand, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnDeregisterDevice(
        Notifications_DeregisterDevice command, CancellationToken cancellationToken)
    {
        var (session, deviceId) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var existingDevices = await Backend.ListDevices(account.Id, cancellationToken).ConfigureAwait(false);
        if (existingDevices.All(d => d.DeviceId != deviceId)) {
            Log.LogWarning("OnDeregisterDevice: non-existing device");
            return;
        }
        var registerDeviceCommand = new NotificationsBackend_RemoveDevices([deviceId]);
        await Commander.Run(registerDeviceCommand, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnNotifyMembers(
        Notifications_NotifyMembers command, CancellationToken cancellationToken)
    {
        var (session, chatId) = command;
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        var author = chat.Rules.Author.Require();
        chat.Rules.Require(ChatPermissions.Write);
        var account = chat.Rules.Account;

        var entryId = new ChatEntryId(author.ChatId, ChatEntryKind.Text, 0, AssumeValid.Option);
        var changeEntry = new ChatsBackend_ChangeEntry(
            entryId,
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = GetWalleId(author.ChatId),
                SystemEntry = (SystemEntry)new NotifyMembersOption(author.Id, author.ToString()),
            }));

        var textEntry = await Commander.Call(changeEntry, true, cancellationToken).ConfigureAwait(false);

        var notifyCommand = new NotificationsBackend_NotifyMembers(account.Id, chatId, textEntry.LocalId - 1);
        await Commander.Run(notifyCommand, cancellationToken).ConfigureAwait(false);

        static AuthorId GetWalleId(ChatId chatId)
            => new(chatId, Constants.User.Walle.AuthorLocalId, AssumeValid.Option);
    }

    // Private methods

    private static Exception Unauthorized()
        => StandardError.Unauthorized("You can access only your own notifications.");
}
