using ActualChat.Notification.Backend;
using ActualChat.Notification.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Notification;

public class Notifications : DbServiceBase<NotificationDbContext>, INotifications, INotificationsBackend
{
    private readonly IAuth _auth;
    private readonly MomentClockSet _clocks;

    public Notifications(IServiceProvider services, IAuth auth, MomentClockSet clocks) : base(services)
    {
        _auth = auth;
        _clocks = clocks;
    }

    // [ComputeMethod]
    public async Task<Device[]> GetDevices(string userId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbDevices = await dbContext.Devices
            .Where(d => d.UserId == userId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var entries = dbDevices.Select(d => d.ToModel()).ToArray();
        return entries;
    }

    // [CommandHandler]
    public virtual async Task RegisterDevice(INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var device = context.Operation().Items.Get<DbDevice>()!;
            _ = GetDevices(device.UserId, default);
            return;
        }

        var (session, deviceId, deviceType) = command;
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        var userId = user.IsAuthenticated ? user.Id.ToString() : "";

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        var existingDbDevice = await dbContext.Devices
            .FindAsync(new object?[] { deviceId }, cancellationToken)
            .ConfigureAwait(false);

        var dbDevice = existingDbDevice;
        if (dbDevice == null) {
            dbDevice = new DbDevice {
                Id = deviceId,
                Type = deviceType,
                UserId = userId,
                Version = VersionGenerator.NextVersion(),
                CreatedAt = _clocks.SystemClock.Now,
            };
            dbContext.Add(dbDevice);
        }
        else
            dbDevice.AccessedAt = _clocks.SystemClock.Now;

        dbContext.Add(dbDevice);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation().Items.Set(dbDevice);
    }

    // [CommandHandler]
    public virtual async Task SubscribeToChat(INotifications.SubscribeToChatCommand command, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
