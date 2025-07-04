using ActualChat.Chat.Module;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace ActualChat.Chat;

public class Translator(IServiceProvider services, [ServiceKey] string serviceKey = Constants.Translation.ServiceKey)
{
    public const string PromptHash = "XYoJnmiu114NtlK7QCi8nGI0rP0zARJ3yvjYOBpQ90A";

    private IServiceProvider Services { get; } = services;

    private string ServiceKey { get; } = serviceKey;

    [field: AllowNull, MaybeNull]
    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();

    [field: AllowNull, MaybeNull]
    private string PromptTemplateString => field ??= File.ReadAllText(Settings.Translation.PromptFile).RequireNonEmpty();

    [field: AllowNull, MaybeNull]
    private Kernel Kernel => field ??= Services.GetRequiredService<Kernel>();

    [field: AllowNull, MaybeNull]
    private IChatCompletionService Completion
        => field ??= Kernel.GetRequiredService<IChatCompletionService>(ServiceKey);

    [field: AllowNull, MaybeNull]
    private IPromptTemplate PromptTemplate => field ??= BuildPromptTemplate();

    public async Task<string> Translate(
        string textToTranslate,
        Language targetLanguage,
        string context = "",
        CancellationToken cancellationToken = default)
    {
        textToTranslate.RequireNonEmpty();
        if (!Settings.IsTranslationEnabled)
            return textToTranslate;

        var arguments = new KernelArguments {
            { "TargetLanguage", $"{targetLanguage.Id} ({targetLanguage.Title})" },
            { "ContextSeparator", Settings.Translation.ContextSeparator },
            { "NoTranslationNeeded", Constants.Chat.NoTranslationNeededText },
        };
        var systemMessage = await PromptTemplate
            .RenderAsync(Kernel, arguments, cancellationToken)
            .ConfigureAwait(false);

        var text =
            $"""
             {context}.

             {Settings.Translation.ContextSeparator}
             {textToTranslate}
             """;

        var executionSettings = new OpenAIPromptExecutionSettings {
            Temperature = 0,
            ChatSystemPrompt = systemMessage.Trim(),
        };
        var chatHistory = new ChatHistory();
        chatHistory.AddUserMessage(text);

        var response = await Completion
            .GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                Kernel,
                cancellationToken)
            .ConfigureAwait(false);
        var result = response.Content ?? "";
        return OrdinalIgnoreCaseEquals(result, Constants.Chat.NoTranslationNeededText)
            ? textToTranslate // If the translation is not needed, return the original text
            : result;
    }

    private IPromptTemplate BuildPromptTemplate()
    {
        var promptTemplateFactory = new KernelPromptTemplateFactory();
        var promptTemplateConfig = new PromptTemplateConfig(PromptTemplateString) {
            InputVariables = [
                new InputVariable {
                    Name = "TargetLanguage",
                    IsRequired = true,
                },
                new InputVariable {
                    Name = "ContextSeparator",
                    IsRequired = true,
                },
                new InputVariable {
                    Name = "NoTranslationNeeded",
                    IsRequired = true,
                },
            ],
        };
        return promptTemplateFactory.Create(promptTemplateConfig);
    }
}
