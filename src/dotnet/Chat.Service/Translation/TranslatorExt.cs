namespace ActualChat.Chat;

public static class TranslatorExt
{
    public static async Task<Dictionary<ChatEntryId, ApiArray<Language>>> DetectLanguages(this Translator translator, IReadOnlyList<ChatEntry> entries, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var detectionCts = cancellationToken.CreateLinkedTokenSource(timeout);
        var languageBulk = await translator
            .DetectLanguages([..entries.Select(entry => entry.Content)], detectionCts.Token)
            .ConfigureAwait(false);
        return entries.Zip(languageBulk, (entry, languages) => (entry, languages))
            .ToDictionary(x => x.entry.Id, x => x.languages);
    }
}
