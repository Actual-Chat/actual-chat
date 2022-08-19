using Stl.Fusion.Extensions;

namespace ActualChat;

public static class TestFeatures
{
    public class ServerTime : FeatureDef<Moment>, IServerFeatureDef
    {
        public override async Task<Moment> Compute(IServiceProvider services, CancellationToken cancellationToken)
        {
            var fusionTime = services.GetRequiredService<IFusionTime>();
            var time = await fusionTime.GetUtcNow(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            return time.ToMoment();
        }
    }

    public class ClientUser : FeatureDef<User?>, IClientFeatureDef
    {
        public override async Task<User?> Compute(IServiceProvider services, CancellationToken cancellationToken)
        {
            var session = services.GetRequiredService<Session>();
            var auth = services.GetRequiredService<IAuth>();
            var user = await auth.GetUser(session, cancellationToken).ConfigureAwait(false);
            return user;
        }
    }
}
