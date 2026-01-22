using ActualLab.Fusion.Extensions;

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
