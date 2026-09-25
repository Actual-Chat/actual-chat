namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI
{
    // Session temporals expire 10 minutes after their last write
    private static readonly TimeSpan ArrivalRefreshPeriod = TimeSpan.FromMinutes(5);

    private string? _arrival;

    public async Task SetArrival(ArrivalInfo arrival, CancellationToken cancellationToken = default)
    {
        // Overwrites: the last link a guest opened is the one that led them to sign up
        try {
            await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!OwnAccount.Value.IsGuestOrNull())
                return;

            var value = arrival.Format();
            Volatile.Write(ref _arrival, value); // Read by MaintainArrival on the thread pool
            await WriteArrival(value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to set the arrival to {Arrival}", arrival.Format());
        }
    }

    // Private methods

    private async Task MaintainArrival(CancellationToken cancellationToken)
    {
        // Re-writes a guest's arrival until they sign up, so a slow sign-up doesn't lose it to the expiry
        await CaptureLandingArrival(cancellationToken).ConfigureAwait(false);
        while (true) {
            await CpuClock.Delay(ArrivalRefreshPeriod, cancellationToken).ConfigureAwait(false);
            if (Volatile.Read(ref _arrival) is not { } value)
                continue;

            if (!OwnAccount.Value.IsGuestOrNull()) {
                Volatile.Write(ref _arrival, null);
                continue;
            }

            try {
                await WriteArrival(value, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                Log.LogWarning(e, "Failed to refresh the arrival");
            }
        }
    }

    private async Task CaptureLandingArrival(CancellationToken cancellationToken)
    {
        // Fills only an empty arrival: a link page that already ran must win over the landing URL
        try {
            await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!OwnAccount.Value.IsGuestOrNull())
                return;

            var existing = await Hub.SessionTemporals
                .Get(Session, Constants.SessionTemporals.ArrivalKey, cancellationToken)
                .ConfigureAwait(false);
            if (ArrivalInfo.TryParse(existing, out var existingArrival)) {
                Interlocked.CompareExchange(ref _arrival, existingArrival.Format(), null);
                return;
            }

            var arrival = ArrivalInfo.FromQuery(History.DefaultItem.Url);
            if (arrival is null && Services.GetService<IInstallReferrer>() is { } installReferrer) {
                var query = await installReferrer.GetQuery(cancellationToken).ConfigureAwait(false);
                arrival = ArrivalInfo.FromQuery(query);
            }
            if (arrival is { } vArrival)
                await SetArrival(vArrival, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to capture the landing arrival");
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
