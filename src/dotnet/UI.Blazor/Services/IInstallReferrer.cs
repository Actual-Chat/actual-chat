namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// The store's install referrer query (<c>utm_source=...&amp;utm_campaign=...</c>);
/// registered only on platforms whose store provides one.
/// </summary>
public interface IInstallReferrer
{
    Task<string?> GetQuery(CancellationToken cancellationToken);
}
