namespace ActualChat.Chat;

public class LanguageDetector(IServiceProvider services) : ChatCompletionBasedService(services, Constants.LanguageDetection.ServiceKey)
{
    [field: AllowNull, MaybeNull]
    private LanguageDetectionSerializer Serializer => field ??= Services.GetRequiredService<LanguageDetectionSerializer>();
    [field: AllowNull, MaybeNull]
    private string Prompt => field ??= File.ReadAllText(Settings.LanguageDetection.PromptFile).RequireNonEmpty();

    public async Task<IReadOnlyList<Language[]>> DetectLanguages(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return [];

        var request = $"""
                       ```xml
                       {Serializer.SerializeRequest(texts)}
                       ```
                       """;
        var content = await Ask(Prompt, request, cancellationToken).ConfigureAwait(false);
        try {
            content = content.OrdinalIgnoreCaseReplace("```xml", "").OrdinalReplace("```", "");
            return Serializer.DeserializeResponse(content, texts.Count);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "Could not deserialize language detection response");
            return [..Enumerable.Repeat(Array.Empty<Language>(), texts.Count)];
        }
    }
}
