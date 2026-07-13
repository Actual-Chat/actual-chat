using ActualChat.Notifications.Module;

namespace ActualChat.Notifications;

// ReSharper disable once InconsistentNaming
public sealed class Features_EnableWalkieTalkiePush : FeatureDef<bool>, IServerFeatureDef
{
    public override Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
        => Task.FromResult(services.GetRequiredService<NotificationsSettings>().EnableWalkieTalkiePush);
}
