using ActualChat.AI;

namespace ActualChat.Chat;

public class Translator(IServiceProvider services, [ServiceKey]string serviceKey = Constants.Translation.ServiceKey) : ChatCompletionBasedService(services, serviceKey)
{
    public const string PromptHash = "XYoJnmiu114NtlK7QCi8nGI0rP0zARJ3yvjYOBpQ90A";

    [field: AllowNull, MaybeNull]
    private string PromptTemplate => field ??= File.ReadAllText(Settings.Translation.PromptFile).RequireNonEmpty();

    public Task<string> Translate(string textToTranslate, Language targetLanguage, string context = "", CancellationToken cancellationToken = default)
    {
        textToTranslate.RequireNonEmpty();
        if (!Settings.IsTranslationEnabled)
            return Task.FromResult(textToTranslate);

        var prompt = PromptHelpers.BuildPrompt(PromptTemplate,
            ("TargetLanguage", $"{targetLanguage.Id} ({targetLanguage.Title})"),
            ("ContextSeparator", Settings.Translation.ContextSeparator),
            ("NoTranslationNeeded", Constants.Chat.NoTranslationNeededText));
        var text =
            $"""
            {context}.

            {Settings.Translation.ContextSeparator}
            {textToTranslate}
            """;
        return Ask(prompt, text, cancellationToken);
    }
}
