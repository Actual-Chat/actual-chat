using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Fills an empty <see cref="AccountFull.TimeZone"/> from the device's zone, so the digest
/// email gets a schedule without the user ever opening the time zone editor.
/// </summary>
public class AccountTimeZoneSyncer(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub)
{
    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(Sync)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(1, 60), Log)
            .RunIsolated(cancellationToken);

    private async Task Sync(CancellationToken cancellationToken)
    {
        await Hub.BrowserInfo.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        var timeZoneId = await ResolveIanaId(Hub.BrowserInfo.TimeZone, cancellationToken).ConfigureAwait(false);
        if (timeZoneId.IsNullOrEmpty())
            return;

        var cAccount = await Computed
            .Capture(() => Hub.Accounts.GetOwn(Session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        UserId? syncedUserId = null;
        await foreach (var c in cAccount.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            var account = c.Value;
            if (account.IsGuestOrNull() || !account.TimeZone.IsNullOrEmpty() || account.Id == syncedUserId)
                continue;

            // Only an empty value is filled, and only once per account: a zone the user picked is theirs
            var command = new Accounts_Update {
                Session = Session,
                Account = account with { TimeZone = timeZoneId },
                ExpectedVersion = account.Version,
            };
            try {
                await Commander.Call(command, cancellationToken).ConfigureAwait(false);
                syncedUserId = account.Id;
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                // Right after sign-in the account version moves under us (avatar sync etc.), so a stale
                // ExpectedVersion is expected here; the next account change retries with the fresh one
                Log.LogWarning(e, "Failed to save the detected time zone {TimeZone}", timeZoneId);
            }
        }
    }

    private async Task<string> ResolveIanaId(string timeZoneId, CancellationToken cancellationToken)
    {
        // Browsers and MAUI on Android, iOS and macOS report IANA ids; MAUI on Windows reports a Windows id
        if (timeZoneId.IsNullOrEmpty())
            return "";

        var timeZones = Hub.TimeZones;
        if (!timeZoneId.Contains('/'))
            timeZoneId = await timeZones.ConvertWindowsToIana(timeZoneId, cancellationToken).ConfigureAwait(false);
        var knownTimeZones = await timeZones.List("en-US", cancellationToken).ConfigureAwait(false);
        return knownTimeZones.Any(z => z.Id == timeZoneId) ? timeZoneId : "";
    }
}
