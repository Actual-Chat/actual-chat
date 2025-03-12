using ActualChat.Chat.Module;
using ActualChat.Integrations.Anthropic;
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
    private string DetectLanguagesPrompt => field ??= File.ReadAllText(Settings.DetectLanguagesPromptFile);
    [field: AllowNull, MaybeNull]
    private string TranslatePromptTemplate => field ??= File.ReadAllText(Settings.TranslatePromptFile);

    public async Task<IReadOnlyList<ApiArray<Language>>> DetectLanguages(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return [];

        try {
            var requestXml = Serializer.SerializeRequest(texts);
            var content = await Ask(DetectLanguagesPrompt, requestXml, cancellationToken).ConfigureAwait(false);
            content = content.OrdinalIgnoreCaseReplace("```xml", "").OrdinalReplace("```", "");
            return Serializer.DeserializeResponse(content, texts.Count);
        }
        catch (Exception e)
        {
            Log.LogWarning(e, "Could not detect languages in bulk");
            return [.. Enumerable.Repeat(ApiArray.Empty<Language>(), texts.Count)];
        }
    }

    public Task<string> Translate(string text, Language targetLanguage, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return Task.FromResult(text);

        var prompt = PromptUtils.BuildPrompt(TranslatePromptTemplate, ("TargetLanguage", targetLanguage));
        return Ask(prompt, text, cancellationToken);
    }

    private async Task<string> Ask(string instruction, string text, CancellationToken cancellationToken)
    {
        var history = new ChatHistory([
            // new(AuthorRole.System, instruction),
            new (AuthorRole.User, text),
        ]);
        var response = await ChatCompletionService
            .GetChatMessageContentAsync(history, new OpenAIPromptExecutionSettings {
                Temperature = 0,
                ChatSystemPrompt = instruction.Trim().EnsureSuffix(":"),
            }, Kernel, cancellationToken)
            .ConfigureAwait(false);
        return response.Content ?? "";
    }
}
