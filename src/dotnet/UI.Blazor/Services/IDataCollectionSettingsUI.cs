namespace ActualChat.UI.Blazor.Services;

public interface IDataCollectionSettingsUI
{
    Task<bool> IsConfigured(CancellationToken cancellationToken);
    Task UpdateState(bool isEnabled, CancellationToken cancellationToken);
}
