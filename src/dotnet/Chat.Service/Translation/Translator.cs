using System.Text;
using ActualChat.Chat.Module;
using ActualChat.Transcription;
using ActualLab.Diagnostics;
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

    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Services.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.TranscriptionTranslation);

    public async Task<string> Translate(
        string textToTranslate,
        Language targetLanguage,
        TranslationResult[] context,
        CancellationToken cancellationToken = default)
    {
        textToTranslate.RequireNonEmpty();
        if (!Settings.IsTranslationEnabled)
            return textToTranslate;

        var executionSettings = await CreateExecutionSettings(textToTranslate, targetLanguage, cancellationToken).ConfigureAwait(false);
        var chatHistory = BuildRequest(textToTranslate, context);

        var response = await Completion
            .GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                Kernel,
                cancellationToken)
            .ConfigureAwait(false);
        var result = response.Content ?? "";

        // DebugLog?.LogDebug("Translate: {Content} = {TranslatedContent} with [{Context}]", textToTranslate, result, string.Join(',', context));
        return OrdinalIgnoreCaseEquals(result, Constants.Translation.NoTranslationNeededText)
            ? textToTranslate // If the translation is not needed, return the original text
            : result;
    }

    public async IAsyncEnumerable<StringDiff> Stream(string textToTranslate, Language targetLanguage, TranslationResult[] context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        textToTranslate.RequireNonEmpty();
        if (!Settings.IsTranslationEnabled) {
            yield return StringDiff.New(textToTranslate, "");
            yield break;
        }

        var executionSettings = await CreateExecutionSettings(textToTranslate, targetLanguage, cancellationToken).ConfigureAwait(false);
        var chatHistory = BuildRequest(textToTranslate, context);

        await foreach (var diff in StreamTranslation().ConfigureAwait(false))
            yield return diff;

        yield break;

        async IAsyncEnumerable<StringDiff> StreamTranslation()
        {
            var sb = new StringBuilder();
            var last = "";
            var stream = Completion
                .GetStreamingChatMessageContentsAsync(
                    chatHistory,
                    executionSettings,
                    Kernel,
                    cancellationToken);
            await foreach (var response in stream.ConfigureAwait(false)) {
                var suffix = response.Content;
                if (suffix.IsNullOrEmpty())
                    continue;

                // DebugLog?.LogDebug("Stream: {TranslatedContent} with [{Context}]", suffix, string.Join(',', context));

                sb.Append(suffix);
                var translatedText = sb.ToString().Trim();
                if (OrdinalEquals(translatedText, Constants.Translation.NoTranslationNeededText))
                    yield break;

                if (Constants.Translation.NoTranslationNeededText.OrdinalStartsWith(translatedText))
                    continue; // wait for the whole NO_TRANSLATION_NEEDED

                yield return StringDiff.New(translatedText, last);

                last = translatedText;
            }
        }
    }

    // Private methods

    private async Task<OpenAIPromptExecutionSettings> CreateExecutionSettings(
        string textToTranslate,
        Language targetLanguage,
        CancellationToken cancellationToken)
    {
        var arguments = new KernelArguments {
            { "TargetLanguage", $"{targetLanguage.Title}" },
        };
        var systemMessage = await PromptTemplate
            .RenderAsync(Kernel, arguments, cancellationToken)
            .ConfigureAwait(false);

        return new OpenAIPromptExecutionSettings {
            Temperature = 0.1,
            ChatSystemPrompt = systemMessage.Trim(),
            MaxTokens = Math.Min(textToTranslate.Length * 8,
                Settings.Translation.OpenAIModelMaxTokens), // estimate for the response length
            FrequencyPenalty = 1.0,
            ResponseFormat = "text",
        };
    }

    private static ChatHistory BuildRequest(string textToTranslate, TranslationResult[] context)
    {
        var chatHistory = new ChatHistory();
        foreach (var (text, translated) in context) {
            if (string.IsNullOrWhiteSpace(text))
                continue;
            if (string.IsNullOrWhiteSpace(translated))
                continue;

            chatHistory.AddUserMessage(text);
            chatHistory.AddAssistantMessage(translated);
        }
        chatHistory.AddUserMessage(textToTranslate);
        return chatHistory;
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
            ],
        };
        return promptTemplateFactory.Create(promptTemplateConfig);
    }
}
