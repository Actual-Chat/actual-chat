using ActualLab.Locking;

namespace ActualChat.UI.Blazor.Services;

// Every ad or link attribution the device sees is sent to the server, for new and existing users alike.
// A guest has no account to attach them to yet, so they wait on the device until the user signs in.

public partial class AccountUI
{
    private const string PendingAttributionsStorageKey = "Attribution.Pending";
    // Not the arrival's flag: installs that read the referrer before attributions existed read it once more
    private const string AttributionInstallReferrerReadStorageKey = "Attribution.InstallReferrerRead";
    private const int MaxPendingAttributions = 10;
    private const int MaxAnalyticsClientAttempts = 3;
    private static readonly TimeSpan AnalyticsClientRetryDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AttributionsStartDelay = TimeSpan.FromSeconds(3);

    private readonly AsyncLock _attributionLock = new();
    private (AttributionOrigin Origin, string Raw)? _installReferrer;
    private bool _isWatchingLocation;
    private (UserId UserId, AnalyticsClientKind Kind, string Id)? _reportedAnalyticsClient;

    public async Task AddAttribution(
        AttributionOrigin origin, string raw, CancellationToken cancellationToken = default)
    {
        // Measurement only: nothing here may fail the caller
        try {
            if (AttributionParser.Parse(origin, raw) is null)
                return;

            await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(true);
            using var _ = await _attributionLock.Lock(cancellationToken).ConfigureAwait(false);
            var pending = await ReadPendingAttributions().ConfigureAwait(false);
            pending.RemoveAll(p => p.Origin == origin && p.Raw == raw);
            pending.Add(new PendingAttribution(origin, raw, Clocks.SystemClock.Now));
            if (pending.Count > MaxPendingAttributions)
                pending.RemoveRange(0, pending.Count - MaxPendingAttributions);
            await WritePendingAttributions(pending).ConfigureAwait(false);
            await SendPendingAttributionsUnsafe(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to add the {Origin} attribution", origin);
        }
    }

    // Private methods

    private async Task CaptureAttributions(CancellationToken cancellationToken)
    {
        await WhenAttributionsCanStart(cancellationToken).ConfigureAwait(true);
        if (!_isWatchingLocation) {
            // A deep link or a campaign link opened while the app runs; the local URL dedupes the landing one
            _isWatchingLocation = true;
            History.LocationChanged += (_, _) => {
                var url = History.Uri;
                if (url.Contains('?'))
                    _ = AddAttribution(AttributionOrigin.Url, url, CancellationToken.None);
            };
        }
        await AddAttribution(AttributionOrigin.Url, History.DefaultItem.Url, cancellationToken)
            .ConfigureAwait(false);
        await CaptureInstallReferrer(cancellationToken).ConfigureAwait(false);
        await SendPendingAttributions(cancellationToken).ConfigureAwait(false);
    }

    private async Task CaptureInstallReferrer(CancellationToken cancellationToken)
    {
        // Once per install; a thrown error leaves it unread, so the next start tries again
        try {
            // Registered only where a store provides one
            if (Services.GetService<IInstallReferrer>() is not { } installReferrer)
                return;

            var isRead = await LocalStorage.GetString(AttributionInstallReferrerReadStorageKey).ConfigureAwait(false);
            if (isRead is not null)
                return;

            var raw = await installReferrer.Get(cancellationToken).ConfigureAwait(false);
            await LocalStorage.SetString(AttributionInstallReferrerReadStorageKey, "1").ConfigureAwait(true);
            if (raw is null)
                return;

            _installReferrer = (installReferrer.Origin, raw);
            await AddAttribution(installReferrer.Origin, raw, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to read the install referrer");
        }
    }

    private async Task MaintainAnalyticsClient(CancellationToken cancellationToken)
    {
        // The server sends milestones under this id; a new user consents during onboarding, mid-session
        await WhenAttributionsCanStart(cancellationToken).ConfigureAwait(true);
        var appSettings = Hub.UserSettingsUI.UserAppSettings();
        var cAppSettings0 = await Computed
            .Capture(() => appSettings.Get(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var changes = cAppSettings0.Changes(FixedDelayer.YieldUnsafe, cancellationToken);
        await foreach (var cAppSettings in changes.ConfigureAwait(false)) {
            var (settings, error) = cAppSettings;
            if (error is null && settings?.IsDataCollectionEnabled == true)
                await ReportAnalyticsClient(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WhenAttributionsCanStart(CancellationToken cancellationToken)
    {
        // Keeps JS interop, local storage and the iOS AdServices request off the startup path
        await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(true);
        await Hub.LoadingUI.WhenRendered.WaitAsync(cancellationToken).ConfigureAwait(true);
        await CpuClock.Delay(AttributionsStartDelay, cancellationToken).ConfigureAwait(true);
    }

    private async Task ReportAnalyticsClient(CancellationToken cancellationToken)
    {
        try {
            // The SDK may get its id a moment after consent is given
            for (var attempt = 1; attempt <= MaxAnalyticsClientAttempts; attempt++) {
                var account = OwnAccount.Value;
                if (account.IsGuestOrNull())
                    return;

                var client = await Hub.DataCollectionSettingsUI
                    .GetAnalyticsClientId(cancellationToken)
                    .ConfigureAwait(true);
                if (client is { } vClient) {
                    var reported = (account.Id, vClient.Kind, vClient.Id);
                    if (_reportedAnalyticsClient == reported)
                        return;

                    var setAnalyticsClientCmd = new Usage_SetAnalyticsClient {
                        Session = Session,
                        Kind = vClient.Kind,
                        ClientId = vClient.Id,
                    };
                    await Hub.Commander.Call(setAnalyticsClientCmd, cancellationToken).ConfigureAwait(true);
                    _reportedAnalyticsClient = reported;
                    return;
                }

                await CpuClock.Delay(AnalyticsClientRetryDelay, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to report the analytics client id");
        }
    }

    private async Task SendPendingAttributions(CancellationToken cancellationToken)
    {
        try {
            using var _ = await _attributionLock.Lock(cancellationToken).ConfigureAwait(false);
            await SendPendingAttributionsUnsafe(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to send the pending attributions");
        }
    }

    private async Task SendPendingAttributionsUnsafe(CancellationToken cancellationToken)
    {
        // Runs under _attributionLock; an item is dropped only once the server has it
        if (OwnAccount.Value.IsGuestOrNull())
            return;

        var pending = await ReadPendingAttributions().ConfigureAwait(true);
        while (pending.Count > 0) {
            var (origin, raw, seenAt) = pending[0];
            var addAttributionCmd = new Usage_AddAttribution {
                Session = Session,
                Origin = origin,
                Raw = raw,
                SeenAt = seenAt,
            };
            await Hub.Commander.Call(addAttributionCmd, cancellationToken).ConfigureAwait(true);
            pending.RemoveAt(0);
            await WritePendingAttributions(pending).ConfigureAwait(true);
        }
    }

    private async Task<List<PendingAttribution>> ReadPendingAttributions()
    {
        var json = await LocalStorage.GetString(PendingAttributionsStorageKey).ConfigureAwait(false);
        if (json.IsNullOrEmpty())
            return [];

        try {
            return SystemJsonSerializer.Default.Read<List<PendingAttribution>>(json) ?? [];
        }
        catch (Exception e) {
            Log.LogWarning(e, "Dropping unreadable pending attributions");
            return [];
        }
    }

    private Task WritePendingAttributions(List<PendingAttribution> pending)
        => pending.Count == 0
            ? LocalStorage.RemoveItem(PendingAttributionsStorageKey)
            : LocalStorage.SetString(PendingAttributionsStorageKey, SystemJsonSerializer.Default.Write(pending));

    // Nested types

    private sealed record PendingAttribution(AttributionOrigin Origin, string Raw, Moment SeenAt);
}
