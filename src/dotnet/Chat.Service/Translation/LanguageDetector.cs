namespace ActualChat.Chat;

public class LanguageDetector(IServiceProvider services) : ChatCompletionBasedService(services, Constants.LanguageDetection.ServiceKey)
{
    [field: AllowNull, MaybeNull]
    private string Prompt => field ??= File.ReadAllText(Settings.LanguageDetection.PromptFile).RequireNonEmpty();

    public async Task<IReadOnlyList<Language>> DetectLanguages(string content, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return [];

        var response = await Ask(Prompt, content, cancellationToken).ConfigureAwait(false);
        try {
            return response.OrdinalIgnoreCaseReplace("```json", "")
                .OrdinalReplace("```", "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Language.ParseNullable)
                .SkipNullItems()
                .ToList();
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken))
        {
            Log.LogError(e, "Could not parse language detection response:");
            return [];
        }
    }
}
