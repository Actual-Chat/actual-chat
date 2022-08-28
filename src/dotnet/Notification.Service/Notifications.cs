using ActualChat.Chat;
using ActualChat.Notification.Backend;
using ActualChat.Notification.Db;
using ActualChat.Users;
using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Notification;

public partial class Notifications : DbServiceBase<NotificationDbContext>, INotifications, INotificationsBackend
{
    private IAuth Auth { get; }
    private FirebaseMessaging FirebaseMessaging { get; }
    private IAccountsBackend AccountsBackend { get; }
    private IChatAuthorsBackend ChatAuthorsBackend { get; }
    private IMarkupParser MarkupParser { get; }
    private UriMapper UriMapper { get; }

    public Notifications(IServiceProvider services) : base(services)
    {
        Auth = services.GetRequiredService<IAuth>();
        FirebaseMessaging = services.GetRequiredService<FirebaseMessaging>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        ChatAuthorsBackend = services.GetRequiredService<IChatAuthorsBackend>();
        MarkupParser = services.GetRequiredService<IMarkupParser>();
        UriMapper = services.GetRequiredService<UriMapper>();
    }

    // [ComputeMethod]
    public virtual async Task<ChatNotificationStatus> GetStatus(Session session, string chatId, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return ChatNotificationStatus.NotSubscribed;

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        string userId = user.Id;
        var isDisabledSubscription = await dbContext.MutedChatSubscriptions
            .AnyAsync(d => d.UserId == userId && d.ChatId == chatId, cancellationToken)
            .ConfigureAwait(false);
        return isDisabledSubscription ? ChatNotificationStatus.NotSubscribed : ChatNotificationStatus.Subscribed;
    }

    // [CommandHandler]
    public virtual async Task RegisterDevice(INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var device = context.Operation().Items.Get<DbDevice>();
            var isNew = context.Operation().Items.GetOrDefault(false);
            if (isNew && device != null)
                _ = ListDevices(device.UserId, default);
            return;
        }

        var (session, deviceId, deviceType) = command;
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user == null)
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
                UserId = user.Id,
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

    // [CommandHandler]
    public virtual async Task SetStatus(INotifications.SetStatusCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        var (session, chatId, mustSubscribe) = command;
        if (Computed.IsInvalidating()) {
            var invWasMuted = context.Operation().Items.GetOrDefault(false);
            if (invWasMuted == mustSubscribe) {
                _ = GetStatus(session, chatId, default);
                _ = ListSubscriberIds(chatId, default);
            }
            return;
        }

        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return;

        string userId = user.Id;
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbMutedSubscription = await dbContext.MutedChatSubscriptions
            .ForUpdate()
            .FirstOrDefaultAsync(cs => cs.UserId == userId && cs.ChatId == chatId, cancellationToken)
            .ConfigureAwait(false);

        var isMutedSubscription = dbMutedSubscription != null;
        context.Operation().Items.Set(isMutedSubscription);
        if (isMutedSubscription != mustSubscribe)
            return;

        if (mustSubscribe) {
            dbContext.Remove(dbMutedSubscription!);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else {
            dbMutedSubscription = new DbMutedChatSubscription {
                Id = Ulid.NewUlid().ToString(),
                UserId = userId,
                ChatId = chatId,
                Version = VersionGenerator.NextVersion(),
            };
            dbContext.Add(dbMutedSubscription);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
