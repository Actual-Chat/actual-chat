namespace ActualChat.Users.AppStores;

/// <summary>
/// Maps an <see cref="AppKind"/> to the store that publishes it.
/// It's a class rather than a switch so tests can script the probes.
/// </summary>
public class StoreProbes(IServiceProvider services)
{
    private IServiceProvider Services { get; } = services;
    private AppleStoreProbe Apple => field ??= Services.GetRequiredService<AppleStoreProbe>();
    private GoogleStoreProbe Play => field ??= Services.GetRequiredService<GoogleStoreProbe>();
    private MicrosoftStoreProbe Microsoft => field ??= Services.GetRequiredService<MicrosoftStoreProbe>();

    public virtual IStoreProbe? Get(AppKind appKind)
        // MacOS shares the iOS record: Mac Catalyst is a universal purchase on the same App ID
        => appKind switch {
            AppKind.Ios or AppKind.MacOS => Apple,
            AppKind.Android => Play,
            AppKind.Windows => Microsoft,
            _ => null,
        };
}
