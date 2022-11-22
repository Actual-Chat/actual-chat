using ActualChat.Notification.Backend;
using ActualChat.Notification.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Notification;

public class Notifications : DbServiceBase<NotificationDbContext>, INotifications
{
    private IAccounts Accounts { get; }
    private INotificationsBackend Backend { get; }

    public Notifications(IServiceProvider services)
        : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        Backend = services.GetRequiredService<INotificationsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListRecentNotificationIds(
        Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).Require().ConfigureAwait(false);
        return await Backend.ListRecentNotificationIds(account.Id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<NotificationEntry> GetNotification(
        Session session, Symbol notificationId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).Require().ConfigureAwait(false);
        return await Backend.GetNotification(account.Id, notificationId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task HandleNotification(
        INotifications.HandleNotificationCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            _ = GetNotification(command.Session, command.NotificationId, default);
            _ = ListRecentNotificationIds(command.Session, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbNotification = await dbContext.Notifications
            .ForUpdate()
            .SingleOrDefaultAsync(x => x.Id == command.NotificationId, cancellationToken)
            .ConfigureAwait(false);
        if (dbNotification == null)
            throw new InvalidOperationException("Notification doesn't exist.");

        dbNotification.HandledAt = Clocks.SystemClock.Now;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task RegisterDevice(INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var device = context.Operation().Items.Get<DbDevice>();
            var isNew = context.Operation().Items.GetOrDefault(false);
            if (isNew && device != null)
                _ = Backend.ListDevices(new UserId(device.UserId), default);
            return;
        }

        var (session, deviceId, deviceType) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return;

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        var existingDbDevice = await dbContext.Devices
            .ForUpdate()
            .SingleOrDefaultAsync(d => d.Id == deviceId, cancellationToken)
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
        context.Operation().Items.Set(dbDevice);
        context.Operation().Items.Set(existingDbDevice == null);
    }
}
