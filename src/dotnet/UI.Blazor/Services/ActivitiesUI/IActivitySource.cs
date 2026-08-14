namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// One domain's contribution to <see cref="ActivitySet"/>; implementations are Fusion
/// compute services registered both as themselves and as <see cref="IActivitySource"/>.
/// </summary>
public interface IActivitySource : IComputeService
{
    [ComputeMethod]
    Task<ActivityInfo?> GetActivity(CancellationToken cancellationToken);
}
