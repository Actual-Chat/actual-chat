namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once InconsistentNaming
public sealed class Features_EnableSpeechCoach : FeatureDef<bool>, IClientFeatureDef
{
    // The server decides: the chat-side master switch and the rollout rule
    public override Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
        => services.GetRequiredService<ICoach>().IsEnabled(services.Session(), cancellationToken);
}
