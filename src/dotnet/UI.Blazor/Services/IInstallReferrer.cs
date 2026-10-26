namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// The store's install attribution, in the shape <see cref="AttributionParser"/> reads for <see cref="Origin"/>;
/// registered only on platforms whose store provides one. A thrown error is transient, null is final.
/// </summary>
public interface IInstallReferrer
{
    AttributionOrigin Origin { get; }

    Task<string?> Get(CancellationToken cancellationToken);
}
