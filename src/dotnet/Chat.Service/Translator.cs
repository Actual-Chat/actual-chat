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
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<ApiArray<Language>> DetectLanguages(string text, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return [];

        var languages = await DetectAllLanguages().ConfigureAwait(false);
        if (languages.IsEmpty)
            languages = await DetectSingleLanguage().ConfigureAwait(false);
        return languages;

        async Task<ApiArray<Language>> DetectAllLanguages()
        {
            try {
                var content = await Ask(Settings.DetectAllLanguagesPrompt, text, cancellationToken).ConfigureAwait(false);
                content = content.OrdinalIgnoreCaseReplace("```json", "").OrdinalReplace("```", "");
                return JsonSerializer.Deserialize<ApiArray<Language>>(content);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Could not detect languages");
                return [];
            }
        }

        async Task<ApiArray<Language>> DetectSingleLanguage()
        {
            var content = await Ask(Settings.DetectSingleLanguagePrompt, text, cancellationToken)
                .Catch("", Log, LogLevel.Warning, "Could not detect even a single language")
                .ConfigureAwait(false);
            return Language.TryParse(content.Trim(), out var language) && !language.IsNone ? [language] : [];
        }
    }

    public Task<string> Translate(string text, Language targetLanguage, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return Task.FromResult(text);

        return Ask(string.Format(Settings.TranslatePromptFormat, targetLanguage), text, cancellationToken);
    }

    private async Task<string> Ask(string instruction, string text, CancellationToken cancellationToken)
    {
        instruction = instruction.Trim().EnsureSuffix(":");
        var history = new ChatHistory([
            // new(AuthorRole.System, instruction),
            new (AuthorRole.User, text),
        ]);
        var response = await ChatCompletionService
            .GetChatMessageContentAsync(history, new OpenAIPromptExecutionSettings {
                Temperature = 0,
                ChatSystemPrompt = instruction,
            }, Kernel, cancellationToken)
            .ConfigureAwait(false);
        return response.Content ?? "";
    }
}
