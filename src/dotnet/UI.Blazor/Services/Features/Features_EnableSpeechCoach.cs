namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once InconsistentNaming
public sealed class Features_EnableSpeechCoach : FeatureDef<bool>, IClientFeatureDef
{
    public override Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
        // The server decides: the chat-side master switch and the rollout rule
        => services.GetRequiredService<ICoach>().IsEnabled(services.Session(), cancellationToken);
}
