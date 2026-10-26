namespace ActualChat.UI.Blazor.Services;

public interface IDataCollectionSettingsUI
{
    Task<bool> IsConfigured(CancellationToken cancellationToken);
    Task UpdateState(bool isEnabled, CancellationToken cancellationToken);
    // Null while analytics collection is off, or before its SDK has an id
    Task<(AnalyticsClientKind Kind, string Id)?> GetAnalyticsClientId(CancellationToken cancellationToken);
}
