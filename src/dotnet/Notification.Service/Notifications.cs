using ActualChat.Notification.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Notification;

public class Notifications(IServiceProvider services) : DbServiceBase<NotificationDbContext>(services), INotifications
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private INotificationsBackend Backend { get; } = services.GetRequiredService<INotificationsBackend>();

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
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

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
    public virtual async Task OnRegisterDevice(Notifications_RegisterDevice command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var device = context.Operation.Items.Get<DbDevice>();
            var isNew = context.Operation.Items.GetOrDefault(false);
            if (isNew && device != null)
                _ = Backend.ListDevices(new UserId(device.UserId), default);
            return;
        }

        var (session, deviceId, deviceType) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        var existingDbDevice = await dbContext.Devices.ForUpdate()
            .FirstOrDefaultAsync(d => d.Id == deviceId.Value, cancellationToken)
            .ConfigureAwait(false);

        var dbDevice = existingDbDevice;
        if (dbDevice == null) {
            dbDevice = new DbDevice {
                Id = deviceId,
                Type = deviceType,
                UserId = account.Id,
                Version = VersionGenerator.NextVersion(),
                CreatedAt = Clocks.SystemClock.Now,
            };
            dbContext.Add(dbDevice);
        }
        else
            dbDevice.AccessedAt = Clocks.SystemClock.Now;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.Set(dbDevice);
        context.Operation.Items.Set(existingDbDevice == null);
    }

    // Private methods

    private static Exception Unauthorized()
        => StandardError.Unauthorized("You can access only your own notifications.");
}
