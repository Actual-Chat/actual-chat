using ActualChat.AI;
using ActualLab.IO;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ActualChat.Chat.ML;

public interface IThreadInsightExtractor
{
    Task<ThreadInsight> GetInsight(
        IReadOnlyCollection<ChatEntrySlim> chatEntries,
        CancellationToken cancellationToken);
}

public class ThreadInsightExtractor(ThreadInsightExtractor.Options settings, IServiceProvider services): IThreadInsightExtractor
{
    public class Options
    {
        public FilePath PromptFile { get; set; } = "";
    }

    public const string ServiceKey = ConversationSummarizer.ServiceKey;

    private readonly ChatDialogFormatterOptions _chatDialogFormatterOptions = new () {
        DisplayTimestamp = true,
        DisplayAuthorPerEntry = true,
        UseSquareBracketsFormat = true,
    };

    private Options Settings { get; } = settings;
    private Kernel Kernel => field ??= services.GetRequiredService<Kernel>();
    private IChatCompletionService ChatCompletionService => field ??= Kernel.GetRequiredService<IChatCompletionService>(ServiceKey);
    private IPromptHelpers PromptHelpers => field ??= services.GetRequiredService<IPromptHelpers>();
    private IChatDialogFormatter ChatDialogFormatter => field ??= services.GetRequiredService<IChatDialogFormatter>();
    private ILogger Log => field ??= services.LogFor(GetType());
    private string PromptTemplate => field ??= File.ReadAllText(Settings.PromptFile).Trim();

    public async Task<ThreadInsight> GetInsight(
        IReadOnlyCollection<ChatEntrySlim> chatEntries,
        CancellationToken cancellationToken)
    {
        var promptTemplate = PromptTemplate;
        if (promptTemplate.IsNullOrEmpty())
            throw StandardError.Constraint("Suggest thread title prompt is not configured.");

        var discussion = await ChatDialogFormatter.EntriesToText(chatEntries, _chatDialogFormatterOptions).ConfigureAwait(false);
        var prompt = PromptHelpers.BuildPrompt(
            promptTemplate,
            new Dictionary<string, string>() {
                { "DISCUSSION", discussion.Truncate(10_000) },
            });
        string? reply;
        try {
            reply = await Ask(prompt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to suggest thread title");
            return new ThreadInsight("", "");
        }
        if (reply.IsNullOrEmpty())
            return new ThreadInsight("", "");

        var title = PromptHelpers.GetXmlTagValue(reply, "title").Trim().NullIfEmpty() ?? "";
        var description = PromptHelpers.GetXmlTagValue(reply, "description").Trim().NullIfEmpty() ?? "";

        return new ThreadInsight(title, description);
    }

    private async Task<string?> Ask(string prompt, CancellationToken cancellationToken)
    {
        var response = await ChatCompletionService
            .GetChatMessageContentAsync(prompt, null, Kernel, cancellationToken)
            .ConfigureAwait(false);
        return response.Content;
    }
}

public record ThreadInsight(string Title, string Description);

public class ThreadInsightExtractorStub : IThreadInsightExtractor
{
    public Task<ThreadInsight> GetInsight(
        IReadOnlyCollection<ChatEntrySlim> chatEntries,
        CancellationToken cancellationToken)
        => Task.FromResult(new ThreadInsight("", ""));
}
