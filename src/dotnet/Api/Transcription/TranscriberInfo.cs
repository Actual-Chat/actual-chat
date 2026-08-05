namespace ActualChat.Transcription;

// An empty Languages / DetectLanguages set means "no declared restriction", not "nothing supported" —
// several providers cover far more languages than Languages.All enumerates.

/// <summary>
/// Declares what a single transcriber configuration can do, so that provider selection
/// can filter and rank candidates without calling them.
/// </summary>
public sealed record TranscriberInfo
{
    public TranscriberId Id { get; init; } = TranscriberId.None;
    public TranscriberKind Kind { get; init; }
    public ApiSet<Language> Languages { get; init; } = new();
    public ApiSet<Language> DetectLanguages { get; init; } = new();
    public bool IsLanguageDetectionSupported { get; init; }
    public TranscriptionContextPolicy? ContextPolicy { get; init; }
    public TranscriberInfo? Retranscriber { get; init; }

    public bool IsSupported(Language language)
        => Languages.Count == 0 || Languages.Contains(language);

    public bool IsDetectionSupported(IReadOnlyCollection<Language> candidates)
    {
        if (!IsLanguageDetectionSupported)
            return false;
        if (DetectLanguages.Count == 0)
            return true;

        foreach (var candidate in candidates)
            if (!DetectLanguages.Contains(candidate))
                return false;

        return true;
    }
}
