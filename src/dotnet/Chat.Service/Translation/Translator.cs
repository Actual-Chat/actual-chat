using System.Text;
using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Diagnostics;
using ActualChat.Module;
using ActualChat.Transcription;
using ActualLab.Diagnostics;
using ActualLab.IO;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace ActualChat.Chat;

public class Translator(IServiceProvider services, [ServiceKey] string serviceKey = Constants.Translation.ServiceKey)
{
    private IServiceProvider Services { get; } = services;

    private string ServiceKey { get; } = serviceKey;

    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();

    private CoreServerSettings CoreServerSettings => field ??= Services.GetRequiredService<CoreServerSettings>();

    private string PromptTemplateString => field ??= File.ReadAllText(GetPromptFilePath()).RequireNonEmpty();

    private Kernel Kernel => field ??= Services.GetRequiredService<Kernel>();

    private IChatCompletionService Completion
        => field ??= Kernel.GetRequiredService<IChatCompletionService>(ServiceKey);

    private IPromptTemplate PromptTemplate => field ??= BuildPromptTemplate();

    protected ILogger Log => field ??= Services.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.TranscriptionTranslation);

    public virtual async Task<string> Translate(
        string textToTranslate,
        Language targetLanguage,
        TranslationResult[] context,
        string? contextHint = null,
        CancellationToken cancellationToken = default)
    {
        textToTranslate.RequireNonEmpty();
        if (!Settings.IsTranslationEnabled)
            return textToTranslate;

        using var activity = CoreServerInstruments.ActivitySource.StartActivity(GetType(), activityKind: ActivityKind.Client);
        try {
            var executionSettings = CreateExecutionSettings(textToTranslate, targetLanguage);
            var chatHistory = await BuildRequest(textToTranslate, targetLanguage, context, contextHint, cancellationToken).ConfigureAwait(false);

            var response = await Completion
                .GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings,
                    Kernel,
                    cancellationToken)
                .ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            var result = response.Content ?? "";
            if (!textToTranslate.EndsWith(" .") && result.EndsWith(" ."))
                result = result[..^2];

            // DebugLog?.LogDebug("Translate: {Content} = {TranslatedContent} with [{Context}]", textToTranslate, result, string.Join(',', context));
            return string.Equals(result, Constants.Translation.NoTranslationNeededText, StringComparison.OrdinalIgnoreCase)
                ? textToTranslate // If the translation is not needed, return the original text
                : result;
        }
        catch (Exception e) {
            activity?.Finalize(e, cancellationToken);
            throw;
        }
    }

    public virtual async IAsyncEnumerable<StringDiff> Stream(
        string textToTranslate,
        Language targetLanguage,
        TranslationResult[] context,
        string? contextHint = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        textToTranslate.RequireNonEmpty();
        if (!Settings.IsTranslationEnabled) {
            yield return StringDiff.New(textToTranslate, "");
            yield break;
        }

        var executionSettings = CreateExecutionSettings(textToTranslate, targetLanguage);
        var chatHistory = await BuildRequest(textToTranslate, targetLanguage, context, contextHint, cancellationToken).ConfigureAwait(false);

        await foreach (var diff in StreamTranslation().ConfigureAwait(false))
            yield return diff;

        yield break;

        async IAsyncEnumerable<StringDiff> StreamTranslation()
        {
            var sb = new StringBuilder();
            var last = "";
            using var activity = CoreServerInstruments.ActivitySource.StartActivity(GetType(), activityKind: ActivityKind.Client);
            var stream = Completion
                .GetStreamingChatMessageContentsAsync(
                    chatHistory,
                    executionSettings,
                    Kernel,
                    cancellationToken)
                .WithActivity(activity, cancellationToken);
            await foreach (var response in stream.ConfigureAwait(false)) {
                var suffix = response.Content;
                if (suffix.IsNullOrEmpty())
                    continue;

                // DebugLog?.LogDebug("Stream: {TranslatedContent} with [{Context}]", suffix, string.Join(',', context));

                sb.Append(suffix);
                var translatedText = sb.ToString().Trim();
                if (translatedText == Constants.Translation.NoTranslationNeededText)
                    yield break;

                if (Constants.Translation.NoTranslationNeededText.StartsWith(translatedText))
                    continue; // wait for the whole NO_TRANSLATION_NEEDED

                yield return StringDiff.New(translatedText, last);

                last = translatedText;
            }
        }
    }

    // Private methods

    private FilePath GetPromptFilePath()
    {
        var promptFile = ServiceKey switch {
            Constants.Translation.RealtimeServiceKey when !Settings.Translation.RealtimePromptFile.IsEmpty
                => Settings.Translation.RealtimePromptFile,
            Constants.Translation.UITextServiceKey => Settings.Translation.UITextPromptFile,
            _ => Settings.Translation.PromptFile,
        };
        var path = CoreServerSettings.PromptsDir | promptFile;
        if (!path.FileExists) {
            // The prompts dir is deployed separately (configs repo); fall back to the base prompt
            // so a code deploy that outruns the configs deploy degrades instead of failing.
            Log.LogWarning("Prompt file {Path} not found, falling back to the base translation prompt", path.Value);
            path = CoreServerSettings.PromptsDir | Settings.Translation.PromptFile;
        }
        return path;
    }

    private PromptExecutionSettings CreateExecutionSettings(string textToTranslate, Language targetLanguage)
    {
        var isGemini = Completion.GetType().Name.Contains("Gemini");
        var isOpenAI = Completion.GetType().Name.Contains("OpenAI");
        if (Completion is RateLimitedChatCompletionService rateLimited) {
            isGemini = rateLimited.ChatCompletionService.GetType().Name.Contains("Gemini");
            isOpenAI = rateLimited.ChatCompletionService.GetType().Name.Contains("OpenAI");
        }

        // estimate for the response length
        var maxTokens = ServiceKey == Constants.Translation.RealtimeServiceKey
            ? Math.Min(textToTranslate.Length * 8, Settings.Translation.RealtimeOpenAIModelMaxTokens)
            : Math.Min(textToTranslate.Length * 8, Settings.Translation.OpenAIModelMaxTokens);

        if (isOpenAI)
            return new OpenAIPromptExecutionSettings {
                Temperature = 0.1,
                MaxTokens = maxTokens,
                FrequencyPenalty = 0.5,
                TopP = 0.1,
                ReasoningEffort = null,
                ResponseFormat = "text",
            };

        if (isGemini)
            return new GeminiPromptExecutionSettings {
                Temperature = 0.3,
                MaxTokens = maxTokens,
                TopP = 0.8,
                ThinkingConfig = new GeminiThinkingConfig {
                    ThinkingBudget = 0,
                },
                ResponseMimeType = "text/plain",
            };

        throw StandardError.Configuration($"ChatCompletionService = {Completion.GetType()} is not supported.");
    }

    private async ValueTask<ChatHistory> BuildRequest(
        string textToTranslate,
        Language targetLanguage,
        TranslationResult[] context,
        string? contextHint = null,
        CancellationToken cancellationToken = default)
    {
        var chatHistory = new ChatHistory();
        var arguments = new KernelArguments {
            { "TargetLanguage", $"{targetLanguage.Title}" },
        };
        var systemMessage = await PromptTemplate
            .RenderAsync(Kernel, arguments, cancellationToken)
            .ConfigureAwait(false);
        if (!contextHint.IsNullOrWhiteSpace())
            systemMessage = $"{systemMessage}\n\n{contextHint}";
        chatHistory.AddSystemMessage(systemMessage);

        // Chat-style few-shot examples would prime UI-text translations toward conversational register
        if (context.Length == 0 && ServiceKey != Constants.Translation.UITextServiceKey) {
            // Provide translation examples to improve the quality of the translation
            if (targetLanguage.IsAnyEnglish) {
                chatHistory.AddUserMessage("Хорошо.");
                chatHistory.AddAssistantMessage("Alright.");
                chatHistory.AddUserMessage("Да");
                chatHistory.AddAssistantMessage("Yes");
                chatHistory.AddUserMessage("Bien");
                chatHistory.AddAssistantMessage("Right");
                // Example showing no translation needed when text is already in target language
                chatHistory.AddUserMessage("Hello, how are you?");
                chatHistory.AddAssistantMessage(Constants.Translation.NoTranslationNeededText);
            }
            if (targetLanguage.IsAnySpanish) {
                chatHistory.AddUserMessage("Good.");
                chatHistory.AddAssistantMessage("Bueno.");
                chatHistory.AddUserMessage("Yep");
                chatHistory.AddAssistantMessage("Sí");
                chatHistory.AddUserMessage("Right");
                chatHistory.AddAssistantMessage("Bien");
                // Example showing no translation needed when text is already in target language
                chatHistory.AddUserMessage("Hola, ¿cómo estás?");
                chatHistory.AddAssistantMessage(Constants.Translation.NoTranslationNeededText);
            }
            if (targetLanguage == Languages.Russian) {
                chatHistory.AddUserMessage("Alright.");
                chatHistory.AddAssistantMessage("Хорошо.");
                chatHistory.AddUserMessage("Yes");
                chatHistory.AddAssistantMessage("Да");
                chatHistory.AddUserMessage("Bien");
                chatHistory.AddAssistantMessage("Хорошо");
                // Example showing no translation needed when text is already in target language
                chatHistory.AddUserMessage("Привет, как дела?");
                chatHistory.AddAssistantMessage(Constants.Translation.NoTranslationNeededText);
            }
        }
        foreach (var (text, translated) in context) {
            if (text.IsNullOrWhiteSpace())
                continue;
            if (translated.IsNullOrWhiteSpace())
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
