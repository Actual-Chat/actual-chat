using System.Net;
using ActualChat.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using HttpOperationException = Microsoft.SemanticKernel.HttpOperationException;

namespace ActualChat.Chat.ML;

public interface IThreadInsightExtractor
{
    Task<ThreadInsight> GetInsight(
        IReadOnlyCollection<TextEntry> chatEntries,
        CancellationToken cancellationToken);
}

public class ThreadInsightExtractor(IServiceProvider services): IThreadInsightExtractor
{
    public const string ServiceKey = ConversationSummarizer.ServiceKey;

    private readonly ChatDialogFormatterOptions _chatDialogFormatterOptions = new () {
        DisplayTimestamp = true,
        DisplayAuthorPerEntry = true,
        UseSquareBracketsFormat = true,
    };

    [field: AllowNull, MaybeNull]
    private Kernel Kernel => field ??= services.GetRequiredService<Kernel>();
    [field: AllowNull, MaybeNull]
    private IChatCompletionService ChatCompletionService => field ??= Kernel.GetRequiredService<IChatCompletionService>(ServiceKey);
    [field: AllowNull, MaybeNull]
    private IPromptUtils PromptUtils => field ??= services.GetRequiredService<IPromptUtils>();
    [field: AllowNull, MaybeNull]
    private IChatDialogFormatter ChatDialogFormatter => field ??= services.GetRequiredService<IChatDialogFormatter>();
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= services.LogFor(GetType());

    public async Task<ThreadInsight> GetInsight(
        IReadOnlyCollection<TextEntry> chatEntries,
        CancellationToken cancellationToken)
    {
        var discussion = await ChatDialogFormatter.EntriesToText(chatEntries, _chatDialogFormatterOptions).ConfigureAwait(false);
         var prompt = PromptUtils.BuildPrompt(
            PromptTemplate,
            new Dictionary<string, string>(StringComparer.Ordinal) {
                { "DISCUSSION", discussion.Truncate(10_000) },
            });
        string? reply;
        try {
            reply = await Ask(prompt, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpOperationException e) when (e.StatusCode == HttpStatusCode.TooManyRequests)  {
            Log.LogDebug(e, "Can't get thread insight. Rate limit exceeded");
            return new ThreadInsight("", "");
        }
        if (reply.IsNullOrEmpty())
            return new ThreadInsight("", "");

        var title = PromptUtils.GetXmlTagValue(reply, "title").Trim().NullIfEmpty() ?? "";
        var description = PromptUtils.GetXmlTagValue(reply, "description").Trim().NullIfEmpty() ?? "";

        return new ThreadInsight(title, description);
    }

    private async Task<string?> Ask(string prompt, CancellationToken cancellationToken)
    {
        var response = await ChatCompletionService
            .GetChatMessageContentAsync(prompt, null, Kernel, cancellationToken)
            .ConfigureAwait(false);
        return response.Content;
    }

    private const string PromptTemplate =
        """
        Summarize the following text discussion of several people in several sentences.
        Generate a catchy, informal title that captures the emotional tone or mood of the conversation (e.g., "Weekend Vibes", "Fishing Time", "No Boredom This Saturday!").
        Write a friendly, lively description (1–4 sentences) that sounds like a teaser or post from a social media feed.
        Provide all results in the language of the discussion.
        Present your final decision in the following XML format:

        <title>
        [A short, catchy, and informal title reflecting the discussion.]
        </title>
        <description>
        [A friendly and emotional description of the conversation.]
        </description>

        Do not add any extra formatting such as code blocks. Output must be valid XML. No extra characters, no missing tags, no formatting issues.

        {{DISCUSSION}}
        """;
}

public record ThreadInsight(string Title, string Description);

public class ThreadInsightExtractorStub : IThreadInsightExtractor
{
    public Task<ThreadInsight> GetInsight(
        IReadOnlyCollection<TextEntry> chatEntries,
        CancellationToken cancellationToken)
        => Task.FromResult(new ThreadInsight("", ""));
}
