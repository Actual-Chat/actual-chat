using ActualChat.Chat.Module;
using ActualChat.Module;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace ActualChat.Chat;

public class LanguageDetector(IServiceProvider services)
{
    public const string PromptHash = "6nqQiUyS73BP81GU40bAR2sLS5OG6P0Hbf5Q2Lo8IKo";

    private IServiceProvider Services { get; } = services;

    private string ServiceKey => Constants.LanguageDetection.ServiceKey;

    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();

    private CoreServerSettings CoreServerSettings => field ??= Services.GetRequiredService<CoreServerSettings>();

    private string Prompt => field ??= File
        .ReadAllText(CoreServerSettings.PromptsDir | Settings.LanguageDetection.PromptFile)
        .RequireNonEmpty();

    private Kernel Kernel => field ??= Services.GetRequiredService<Kernel>();

    private IChatCompletionService Completion
        => field ??= Kernel.GetRequiredService<IChatCompletionService>(ServiceKey);

    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<IReadOnlyList<Language>> DetectLanguages(string content, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return [];

        if (content.IsNullOrWhiteSpace() || !content.Any(char.IsLetter))
            return [];

        var executionSettings = new OpenAIPromptExecutionSettings {
            Temperature = 0,
            ChatSystemPrompt = Prompt.Trim(),
            ResponseFormat =  ChatResponseFormat.ForJsonSchema(
                JsonDocument.Parse(
                """
                {
                    "type": "array",
                    "items": { "type": "string" }
                }
                """).RootElement,
                "languages",
                "The list of languages detected in the text, in the format: [\"en\", \"fr\", \"de\"]"),
        };
        var chatHistory = new ChatHistory();
        chatHistory.AddUserMessage(content);

        var response = await Completion
            .GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                Kernel,
                cancellationToken)
            .ConfigureAwait(false);
        try {
            var result = response.Content ?? "[]";
            var json = result
                .OrdinalIgnoreCaseReplace("```json", "")
                .OrdinalReplace("`", "");
            return SystemJsonSerializer.Default.Read<string[]>(json)
                .Select(Language.ParseNullable)
                .SkipNullItems()
                .ToList();
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "Could not parse language detection response: {Response}", response.Content);
            return [];
        }
    }
}
