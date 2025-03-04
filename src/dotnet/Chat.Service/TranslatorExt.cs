namespace ActualChat.Chat;

public static class TranslatorExt
{
    public static Task<ApiArray<Language>> TryDetectLanguages(
        this Translator translator,
        ChatEntry entry,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (entry.Kind is not ChatEntryKind.Text || entry.Content.IsNullOrEmpty())
            return Task.FromResult(ApiArray<Language>.Empty);

        var cts = cancellationToken.CreateLinkedTokenSource(timeout);
        return translator.DetectLanguages(entry.Content, cts.Token).SuppressCancellation();
    }
}
