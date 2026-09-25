namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI
{
    // Overwrites: the last link a guest opened is the one that led them to sign up
    public async Task SetArrival(ArrivalInfo arrival, CancellationToken cancellationToken = default)
    {
        try {
            await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!OwnAccount.Value.IsGuestOrNull())
                return;

            var command = new SessionTemporals_Set {
                Session = Session,
                Key = Constants.SessionTemporals.ArrivalKey,
                Value = arrival.Format(),
            };
            await Hub.Commander.Call(command, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to set the arrival to {Arrival}", arrival.Format());
        }
    }

    // Private methods

    // Fills only an empty arrival: a link page that already ran must win over the landing URL
    private async Task CaptureLandingArrival(CancellationToken cancellationToken)
    {
        try {
            await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!OwnAccount.Value.IsGuestOrNull())
                return;

            var existing = await Hub.SessionTemporals
                .Get(Session, Constants.SessionTemporals.ArrivalKey, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
                return;

            var arrival = ArrivalInfo.FromQuery(History.DefaultItem.Url);
            if (arrival is null && Services.GetService<IInstallReferrer>() is { } installReferrer) {
                var query = await installReferrer.GetQuery(cancellationToken).ConfigureAwait(false);
                arrival = ArrivalInfo.FromQuery(query);
            }
            if (arrival is { } vArrival)
                await SetArrival(vArrival, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to capture the landing arrival");
        }
    }
}
