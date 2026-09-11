namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once InconsistentNaming
public sealed class Features_EnableAnonymousChat : FeatureDef<bool>, IClientFeatureDef
{
    public static bool IsEnabled => false;

    public override Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
        => Task.FromResult(false);
}
