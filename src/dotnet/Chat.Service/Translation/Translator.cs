using ActualChat.AI;
using ActualChat.Chat.Module;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace ActualChat.Chat;

public class Translator(IServiceProvider services) : IHasServices
{
    public const string ServiceKey = nameof(Translator);

    public IServiceProvider Services => services;
    [field: AllowNull, MaybeNull]
    private Kernel Kernel => field ??= Services.GetRequiredService<Kernel>();
    [field: AllowNull, MaybeNull]
    private IChatCompletionService ChatCompletionService => field ??= Kernel.GetRequiredService<IChatCompletionService>(ServiceKey);
    [field: AllowNull, MaybeNull]
    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();
    [field: AllowNull, MaybeNull]
    private LanguageDetectionSerializer Serializer => field ??= Services.GetRequiredService<LanguageDetectionSerializer>();
    [field: AllowNull, MaybeNull]
    private IPromptUtils PromptUtils => field ??= Services.GetRequiredService<IPromptUtils>();
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= Services.LogFor(GetType());
    [field: AllowNull, MaybeNull]
    private string DetectLanguagesPrompt => field ??= File.ReadAllText(Settings.DetectLanguagesPromptFile).RequireNonEmpty();
    [field: AllowNull, MaybeNull]
    private string TranslatePromptTemplate => field ??= File.ReadAllText(Settings.TranslatePromptFile).RequireNonEmpty();

    public async Task<IReadOnlyList<Language[]>> DetectLanguages(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return [];

        try {
            var request =
                $"""
                ```xml
                {Serializer.SerializeRequest(texts)}
                ```
                """;
            var content = await Ask(DetectLanguagesPrompt, request, cancellationToken).ConfigureAwait(false);
            content = content.OrdinalIgnoreCaseReplace("```xml", "").OrdinalReplace("```", "");
            return Serializer.DeserializeResponse(content, texts.Count);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Could not detect languages in bulk");
            return Enumerable.Repeat(Array.Empty<Language>(), texts.Count).ToArray();
        }
    }

    public Task<string> Translate(string textToTranslate, Language targetLanguage, string context = "", CancellationToken cancellationToken = default)
    {
        textToTranslate.RequireNonEmpty();
        if (!Settings.IsTranslationEnabled)
            return Task.FromResult(textToTranslate);

        var prompt = PromptUtils.BuildPrompt(TranslatePromptTemplate, ("TargetLanguage", $"{targetLanguage.Id} ({targetLanguage.Title})"), ("ContextSeparator", Settings.TranslationContextSeparator));
        var text = $"""
                    {context}
                    {Settings.TranslationContextSeparator}
                    {textToTranslate}
                    """;
        return Ask(prompt, text, cancellationToken);
    }

    private async Task<string> Ask(string instruction, string text, CancellationToken cancellationToken)
    {
        var history = new ChatHistory(new ChatMessageContent[] {
            new (AuthorRole.User, text),
        }.Where(x => !x.Content.IsNullOrEmpty()));
        var response = await ChatCompletionService
            .GetChatMessageContentAsync(history, new OpenAIPromptExecutionSettings {
                Temperature = 0,
                ChatSystemPrompt = instruction.Trim().EnsureSuffix(":"),
            }, Kernel, cancellationToken)
            .ConfigureAwait(false);
        return response.Content ?? "";
    }
}
