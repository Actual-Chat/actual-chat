namespace ActualChat.UI.Blazor.Services;

public interface IAnalyticsUI
{
    Task<bool> IsConfigured(CancellationToken cancellationToken);
    Task UpdateAnalyticsState(bool isEnabled, CancellationToken cancellationToken);
}
