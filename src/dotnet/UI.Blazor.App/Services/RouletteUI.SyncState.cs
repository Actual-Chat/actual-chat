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
    protected virtual async Task<(Symbol, Profile)> GetSelectedProfile(CancellationToken cancellationToken)
    {
        var selectedProfile = await SelectedProfile.Use(cancellationToken).ConfigureAwait(false);
        var selectedProfileId = selectedProfile.Id;
        if (selectedProfileId.IsEmpty)
            return (Symbol.Empty, SpecialProfile.None);

        var profile = await RouletteProfiles.GetOwnProfile(Session, selectedProfileId, cancellationToken).ConfigureAwait(false);
        return (selectedProfileId, profile ?? SpecialProfile.None);
    }

    private async Task SyncSelectedProfile(CancellationToken cancellationToken)
    {
        var cGetProfile0 = await Computed
            .Capture(() =>  GetSelectedProfile(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var changes = cGetProfile0.Changes(FixedDelayer.NextTick, cancellationToken).Skip(1);
        await foreach (var cGetProfile in changes.ConfigureAwait(false)) {
            var (profileId, profile) = cGetProfile.Value;
            if (SelectedProfile.Value.Id != profileId)
                continue; // Skip update if the selected profile id has changed.

            if (profile.Id.IsEmpty)
                DiscardSelectedProfile();
            else
                SelectProfile(profile);
        }
    }
}
