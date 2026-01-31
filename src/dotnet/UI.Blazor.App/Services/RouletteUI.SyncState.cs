using ActualChat.Roulette;

namespace ActualChat.UI.Blazor.App.Services;

partial class RouletteUI
{
    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(SyncSelectedProfile)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(0.5, 3), Log)
            .RunIsolated(StopToken);

    [ComputeMethod]
    protected virtual async Task<(Profile? Cached, Profile? Actual)> GetSelectedProfile(
        CancellationToken cancellationToken)
    {
        var cached = await SelectedProfile.Use(cancellationToken).ConfigureAwait(false);
        if (cached is null)
            return (null, null);

        var actual = await RouletteProfiles.GetOwnProfile(Session, cached.Id, cancellationToken).ConfigureAwait(false);
        return (cached, actual);
    }

    private async Task SyncSelectedProfile(CancellationToken cancellationToken)
    {
        var cGetSelectedProfilePair0 = await Computed
            .Capture(() =>  GetSelectedProfile(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var changes = cGetSelectedProfilePair0.Changes(FixedDelayer.NextTick, cancellationToken).Skip(1);
        await foreach (var cGetProfilePair in changes.ConfigureAwait(false)) {
            var (cached, actual) = cGetProfilePair.Value;
            var cachedId = cached?.Id ?? Symbol.Empty;
            var actualId = actual?.Id ?? Symbol.Empty;
            if (cachedId != actualId)
                continue; // Skip update if the selected profile Id has changed.

            if (actualId.IsEmpty)
                ResetSelectedProfile();
            else
                SelectProfile(actual);
        }
    }
}
