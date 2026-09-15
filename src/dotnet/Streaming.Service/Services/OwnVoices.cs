using ActualChat.Users;

namespace ActualChat.Streaming.Services;

public class OwnVoices(IServiceProvider services) : IOwnVoices
{
    private IServiceProvider Services { get; } = services;
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    private IUserVoicesBackend UserVoicesBackend => field ??= Services.GetRequiredService<IUserVoicesBackend>();
    private VoiceSampleBuilder SampleBuilder => field ??= Services.GetRequiredService<VoiceSampleBuilder>();

    // [ComputeMethod]
    public virtual async Task<OwnVoiceStatus> GetOwnVoiceStatus(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return OwnVoiceStatus.Off;

        var settings = await Services.UserSettingsUI(session)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        var hasExplicitSample = settings.OwnVoiceSampleMediaId != null;
        if (!settings.IsOwnVoiceEnabled)
            return OwnVoiceStatus.Off with { HasExplicitSample = hasExplicitSample };

        var voice = await UserVoicesBackend.Get(account.Id, cancellationToken).ConfigureAwait(false);
        // The status follows the settings, the UserVoice record and the recency lists; the tile reads
        // behind Inspect are isolated by design (ChatsBackendExt.ListEntries), so a new recording in
        // an already-listed chat shows up only after the cache expires - no live countdown
        var (available, failure) = await SampleBuilder
            .Inspect(account.Id, settings, cancellationToken)
            .ConfigureAwait(false);
        var missingDuration = available is { } availableDuration
            ? (Constants.Audio.VoiceSampleMinDuration - availableDuration).Positive()
            : (TimeSpan?)null;
        var status = voice?.Status ?? UserVoiceStatus.None;
        return new OwnVoiceStatus(true, status, failure, missingDuration, hasExplicitSample);
    }
}
