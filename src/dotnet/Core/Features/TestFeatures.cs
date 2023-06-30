using Stl.Fusion.Extensions;

namespace ActualChat;

// ReSharper disable once InconsistentNaming
public class TestFeature_ServerTime : FeatureDef<Moment>, IServerFeatureDef
{
    public override async Task<Moment> Compute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var fusionTime = services.GetRequiredService<IFusionTime>();
        var time = await fusionTime.Now(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        return time;
    }
}

// ReSharper disable once InconsistentNaming
public class TestFeature_ClientUser : FeatureDef<User?>, IClientFeatureDef
{
    public override async Task<User?> Compute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var session = services.GetRequiredService<Session>();
        var auth = services.GetRequiredService<IAuth>();
        var user = await auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        return user;
    }
}
