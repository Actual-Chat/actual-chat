using ActualLab.Locking;

namespace ActualChat.UI.Blazor.Services;

// The arrival lives on the device until the account it led to exists: the server keeps its copy
// (a session temporal) for just 10 minutes after each write, so it is re-sent while it matters.

public partial class AccountUI
{
    private const string ArrivalStorageKey = "Arrival";
    private const string InstallReferrerReadStorageKey = "Arrival.InstallReferrerRead";
    private static readonly TimeSpan ArrivalRefreshPeriod = TimeSpan.FromMinutes(5);

    private readonly AsyncLock _arrivalLock = new();
    private string? _arrival;

    public async Task SetArrival(ArrivalInfo arrival, CancellationToken cancellationToken = default)
    {
        // Overwrites: the last link a guest opened is the one that led them to sign up
        try {
            await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!OwnAccount.Value.IsGuestOrNull())
                return;

            using var _ = await _arrivalLock.Lock(cancellationToken).ConfigureAwait(false);
            var value = arrival.Format();
            _arrival = value;
            await LocalStorage.SetString(ArrivalStorageKey, value).ConfigureAwait(false);
            await WriteArrival(value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to set the arrival to {Arrival}", arrival.Format());
        }
    }

    public async Task PublishArrival(CancellationToken cancellationToken = default)
    {
        try {
            using var _ = await _arrivalLock.Lock(cancellationToken).ConfigureAwait(false);
            if (_arrival is not { } value || !OwnAccount.Value.IsGuestOrNull())
                return;

            await WriteArrival(value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to publish the arrival");
        }
    }

    // Private methods

    private async Task MaintainArrival(CancellationToken cancellationToken)
    {
        await CaptureArrival(cancellationToken).ConfigureAwait(false);
        while (true) {
            await CpuClock.Delay(ArrivalRefreshPeriod, cancellationToken).ConfigureAwait(false);
            await PublishArrival(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CaptureArrival(CancellationToken cancellationToken)
    {
        // A kept arrival, or one a link page already set, wins over the landing URL and the install referrer
        try {
            await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!OwnAccount.Value.IsGuestOrNull()) {
                await ForgetArrival().ConfigureAwait(false);
                return;
            }

            using var _ = await _arrivalLock.Lock(cancellationToken).ConfigureAwait(false);
            if (_arrival is null && ArrivalInfo.TryParse(
                    await LocalStorage.GetString(ArrivalStorageKey).ConfigureAwait(false), out var kept))
                _arrival = kept.Format();
            if (_arrival is null) {
                var arrival = ArrivalInfo.FromQuery(History.DefaultItem.Url)
                    ?? await ReadInstallReferrer(cancellationToken).ConfigureAwait(false);
                if (arrival is { } vArrival) {
                    _arrival = vArrival.Format();
                    await LocalStorage.SetString(ArrivalStorageKey, _arrival).ConfigureAwait(false);
                }
            }
            if (_arrival is { } value)
                await WriteArrival(value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to capture the arrival");
        }
    }

    private async Task<ArrivalInfo?> ReadInstallReferrer(CancellationToken cancellationToken)
    {
        // Once per install: a sign-up after a later sign-out must not be credited to the install campaign
        if (Services.GetService<IInstallReferrer>() is not { } installReferrer)
            return null;
        if (await LocalStorage.GetString(InstallReferrerReadStorageKey).ConfigureAwait(false) is not null)
            return null;

        var query = await installReferrer.GetQuery(cancellationToken).ConfigureAwait(false);
        await LocalStorage.SetString(InstallReferrerReadStorageKey, "1").ConfigureAwait(false);
        return ArrivalInfo.FromQuery(query);
    }

    private async Task ForgetArrival()
    {
        // The account the arrival led to exists now; the server has already consumed its copy
        try {
            using var _ = await _arrivalLock.Lock().ConfigureAwait(false);
            _arrival = null;
            await LocalStorage.RemoveItem(ArrivalStorageKey).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to forget the arrival");
        }
    }

    private Task WriteArrival(string value, CancellationToken cancellationToken)
    {
        var command = new SessionTemporals_Set {
            Session = Session,
            Key = Constants.SessionTemporals.ArrivalKey,
            Value = value,
        };
        return Hub.Commander.Call(command, cancellationToken);
    }
}
