namespace ActualChat.Users.Module;

/// <summary>
/// Store probe targets and timings for <see cref="IAppUpdates"/>.
/// An empty store id disables detection for the app kinds it serves.
/// </summary>
public sealed class AppUpdateSettings
{
    // null means "production instances only" - the dev app isn't in any store
    public bool? IsEnabled { get; set; }
    public string AppleStoreId { get; set; } = "chat.actual.app";
    public string GoogleStoreId { get; set; } = "chat.actual.app";
    public string MicrosoftStoreId { get; set; } = "9N6RWRD9FMS2";
    public TimeSpan RecheckPeriod { get; set; } = TimeSpan.FromMinutes(2);
    // How long a detected release is held back before clients are told about it, which is
    // also what covers any propagation lag between the storefront we probe and the user's
    public TimeSpan AnnounceDelay { get; set; } = TimeSpan.FromHours(1);
    public double ProbeDelayMin { get; set; } = 60;
    public double ProbeDelayMax { get; set; } = 1800;
    public TimeSpan MinProbeInterval { get; set; } = TimeSpan.FromSeconds(50);
    public TimeSpan WasmGracePeriod { get; set; } = TimeSpan.FromMinutes(10);
    // AppKind name -> build version, e.g. { "Android": "2.99.0" } - the QA hook for dev and local
    public IReadOnlyDictionary<string, string> Overrides { get; set; } =
        ImmutableDictionary<string, string>.Empty;
    public RetryDelaySeq ProbeDelays => RetryDelaySeq.Exp(ProbeDelayMin, ProbeDelayMax);
    public string GetStoreId(AppKind appKind)
        => appKind switch {
            AppKind.Ios or AppKind.MacOS => AppleStoreId,
            AppKind.Android => GoogleStoreId,
            AppKind.Windows => MicrosoftStoreId,
            _ => "",
        };
}
