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
    // Indexed by the day of the wait, last entry repeating: a store usually publishes within
    // hours of the deploy, and a wait that has already lasted days is unlikely to end this minute
    public TimeSpan[] RecheckPeriods { get; set; } = [
        TimeSpan.FromMinutes(3),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
    ];
    // How long a detected release is held back before clients are told about it.
    // Absorbs propagation to regional storefronts.
    public TimeSpan AnnounceDelay { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan WasmGracePeriod { get; set; } = TimeSpan.FromMinutes(10);
    // AppKind name -> build version, e.g. { "Android": "2.99.0" } - the QA hook for dev and local
    public IReadOnlyDictionary<string, string> Overrides { get; set; } =
        ImmutableDictionary<string, string>.Empty;

    public TimeSpan GetRecheckPeriod(TimeSpan waitDuration)
    {
        var dayIndex = (int)waitDuration.TotalDays;
        return RecheckPeriods[Math.Clamp(dayIndex, 0, RecheckPeriods.Length - 1)];
    }

    public string GetStoreId(AppKind appKind)
        => appKind switch {
            AppKind.Ios or AppKind.MacOS => AppleStoreId,
            AppKind.Android => GoogleStoreId,
            AppKind.Windows => MicrosoftStoreId,
            _ => "",
        };
}
