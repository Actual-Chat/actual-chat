using ActualChat.AI;
using ActualLab.IO;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ActualChat.Chat.ML;

public interface IChatDigestSummarizer
{
    Task<IReadOnlyCollection<string>> Summarize(
        IReadOnlyCollection<ChatEntry> chatEntries,
        CancellationToken cancellationToken);
}

public class ChatDigestSummarizer(ChatDigestSummarizer.Options settings, IServiceProvider services) : IChatDigestSummarizer
{
    public class Options
    {
        public FilePath PromptFile { get; set; } = "";
    }

    public const string ServiceKey = ConversationSummarizer.ServiceKey;

    private Options Settings { get; } = settings;
    private Kernel Kernel => field ??= services.GetRequiredService<Kernel>();
    private IChatCompletionService ChatCompletionService => field ??= Kernel.GetRequiredService<IChatCompletionService>(ServiceKey);
    private IPromptHelpers PromptHelpers => field ??= services.GetRequiredService<IPromptHelpers>();
    private IChatDialogFormatter ChatDialogFormatter => field ??= services.GetRequiredService<IChatDialogFormatter>();
    private ILogger Log => field ??= services.LogFor(GetType());
    private string Prompt => field ??= File.ReadAllText(Settings.PromptFile).Trim();

    public async Task<IReadOnlyCollection<string>> Summarize(
        IReadOnlyCollection<ChatEntry> chatEntries,
        CancellationToken cancellationToken)
    {
        if (Prompt.IsNullOrEmpty())
            throw StandardError.Constraint("Summarize chat digest prompt is not configured.");

        var text = await ChatDialogFormatter.EntriesToText(chatEntries).ConfigureAwait(false);
        var prompt = PromptHelpers.BuildPrompt(
            Prompt,
            new Dictionary<string, string>(StringComparer.Ordinal) {
                { "DOCUMENT", text.Substring(0, Math.Min(text.Length, 1_000_000)) },
            });
        try {
            var response = await Ask(prompt, cancellationToken).ConfigureAwait(false);
            var summary = PromptHelpers.GetXmlTagValue(response, "summary");
            return summary.Split("\n", StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        catch (Exception ex) {
            Log.LogError(ex, "Summarization error");
            return [];
        }
    }

    private async Task<string> Ask(string prompt, CancellationToken cancellationToken)
    {
        var response = await ChatCompletionService
            .GetChatMessageContentAsync(prompt, null, Kernel, cancellationToken)
            .ConfigureAwait(false);
        return response.Content ?? "";
    }
}

public class ChatDigestSummarizerStub : IChatDigestSummarizer
{
    public Task<IReadOnlyCollection<string>> Summarize(IReadOnlyCollection<ChatEntry> chatEntries, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyCollection<string>>([]);
}
