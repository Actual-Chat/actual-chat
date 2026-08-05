namespace ActualChat.Transcription;

public sealed class TranscriberSelector(ITranscriberRegistry registry, IServiceProvider services)
    : ITranscriberSelector
{
    private ILogger Log { get; } = services.LogFor<TranscriberSelector>();

    public ITranscriber? GetStream(TranscriptionOptions options, TranscriberId preferredId)
    {
        var candidates = registry.ListStream(options, preferredId);
        Log.LogInformation("Stream chain for {Language}: {TranscriberIds}",
            options.Language,
            candidates.Count == 0 ? "<none>" : candidates.Select(x => x.Info.Id.Value).ToDelimitedString());

        return candidates.Count switch {
            0 => null,
            1 => candidates[0],
            _ => new FailoverTranscriber(candidates, Log),
        };
    }

    public IOfflineTranscriber? GetOffline(
        TranscriptionOptions options,
        TranscriberId preferredId,
        TranscriberInfo? streamInfo = null)
    {
        // "Automatic" means the best result end to end, so it always takes the ranked retranscriber:
        // measured on real recordings, an offline pass still fixes words a self-refining stream fused.
        // An explicit pick is taken literally instead - bare "Soniox" has to stay a single pass.
        if (!preferredId.IsNone && !preferredId.IsPair
            && streamInfo is { Retranscriber: null } && !streamInfo.IsOfflinePassNeeded) {
            Log.LogInformation("Retranscription skipped: {TranscriberId} is {Kind}",
                streamInfo.Id, streamInfo.Kind);
            return null;
        }

        var candidates = registry.ListOffline(options, preferredId);
        Log.LogInformation("Retranscription chain: {TranscriberIds}",
            candidates.Count == 0 ? "<none>" : candidates.Select(x => x.Info.Id.Value).ToDelimitedString());

        return candidates.Count switch {
            0 => null,
            1 => candidates[0],
            _ => new FailoverOfflineTranscriber(candidates, Log),
        };
    }
}
