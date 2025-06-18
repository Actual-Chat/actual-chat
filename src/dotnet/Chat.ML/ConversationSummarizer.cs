using System.Net;
using ActualChat.AI;
using Cysharp.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using HttpOperationException = Microsoft.SemanticKernel.HttpOperationException;

namespace ActualChat.Chat.ML;

public interface IConversationSummarizer
{
    Task<ConversationSummarizerResult> Summarize(
        IReadOnlyCollection<TextEntry> chatEntries,
        CancellationToken cancellationToken);
}

public class ConversationSummarizer(IServiceProvider services): IConversationSummarizer
{
    public const string ServiceKey = nameof(ConversationSummarizer);

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
    private IPromptHelpers PromptHelpers => field ??= services.GetRequiredService<IPromptHelpers>();
    [field: AllowNull, MaybeNull]
    private IChatDialogFormatter ChatDialogFormatter => field ??= services.GetRequiredService<IChatDialogFormatter>();
    [field: AllowNull, MaybeNull]
    private IAuthorNameRetriever AuthorNameRetriever => field ??= services.GetRequiredService<IAuthorNameRetriever>();
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= services.LogFor(GetType());

    public async Task<ConversationSummarizerResult> Summarize(
        IReadOnlyCollection<TextEntry> chatEntries,
        CancellationToken cancellationToken)
    {
        var authorIds = chatEntries.Select(c => c.AuthorId).Distinct().ToArray();
        var mentionsMap = await BuildMentionsMap(authorIds).ConfigureAwait(false);
        var discussion = await ChatDialogFormatter.EntriesToText(chatEntries, _chatDialogFormatterOptions).ConfigureAwait(false);
         var prompt = PromptHelpers.BuildPrompt(
            PromptTemplate,
            new Dictionary<string, string>(StringComparer.Ordinal) {
                { "DISCUSSION", discussion.Truncate(100_000) },
                { "MENTIONS_MAP", mentionsMap },
            });
        string? reply;
        try {
            reply = await Ask(prompt, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpOperationException e) when (e.StatusCode == HttpStatusCode.TooManyRequests)  {
            Log.LogDebug(e, "Can't summarize. Rate limit exceeded");
            if (!TryExtractTryAgainInDelay(e.Message, out var postpone))
                postpone = TimeSpan.FromSeconds(45);
            return new ConversationSummarizerResult(e, postpone);
        }
        var firstEntry = chatEntries.First();
        var lastEntry = chatEntries.Last();
        var count = chatEntries.Count;
        if (reply.IsNullOrEmpty())
            return new ConversationSummary($"Summary of {count} entries with range: {firstEntry.LocalId} - {lastEntry.LocalId}",
                "",
                "Failed to retrieve summary");

        var title = PromptHelpers.GetXmlTagValue(reply, "title").Trim().NullIfEmpty()
            ?? $"Summary of {count} entries with range: {firstEntry.LocalId} - {lastEntry.LocalId}";
        var description = PromptHelpers.GetXmlTagValue(reply, "description").Trim().NullIfEmpty() ?? "";
        var summary = PromptHelpers.GetXmlTagValue(reply, "summary").Trim().NullIfEmpty() ?? reply;

        return new ConversationSummary(title, description, summary);
    }

    private async Task<string> BuildMentionsMap(AuthorId[] authorIds)
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        foreach (var authorId in authorIds) {
            if (sb.Length > 0)
                sb.AppendLine();
            var authorName = await AuthorNameRetriever.GetAuthorName(authorId).ConfigureAwait(false);
            sb.Append('[');
            sb.Append(authorName);
            sb.Append("] @");
            var mentionId = MentionId.NewAuthor(authorId);
            sb.Append(mentionId.Value);
        }
        return sb.ToStringAndRelease();
    }

    private static bool TryExtractTryAgainInDelay(string message, out TimeSpan tryAgainInDelay)
    {
        tryAgainInDelay = TimeSpan.Zero;
        const string pleaseTryAgainIn = "Please try again in";
        var index1 = message.IndexOf(pleaseTryAgainIn, StringComparison.Ordinal);
        if (index1 < 0)
            return false;

        var index2 = message.IndexOf(". Visit", index1, StringComparison.Ordinal);
        if (index2 < 0)
            return false;

        var startIndex = index1 + pleaseTryAgainIn.Length;
        var sDelay = message.Substring(startIndex, index2 - startIndex).Trim();
        var index3 = sDelay.FirstIndexOf(char.IsLetter);
        if (index3 < 0)
            return false;

        var sValue = sDelay.Substring(0, index3);
        var units = sDelay.Substring(index3);
        if (!double.TryParse(sValue, CultureInfo.InvariantCulture, out var value))
            return false;

        if (OrdinalEquals(units, "ms"))
            tryAgainInDelay = TimeSpan.FromMilliseconds(value);
        else if (OrdinalEquals(units, "s"))
            tryAgainInDelay = TimeSpan.FromSeconds(value);
        else
            tryAgainInDelay = TimeSpan.FromSeconds(55);

        return true;
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
        Specify what topics have been discussed and key moments.
        Who made commitments and what commitments are.
        Additionally:

        Provide all results in the language of the discussion.
        Provide a title that reflects the essence of the discussion.
        Give a brief description of the discussion (3-4 sentences).
        Provide a summary as list of points with using markdown.
        Summary length should be at least 10% from original discussion.
        To refer to discussion participants in summary and description use special mention format (sequence started with @) instead of name:
        {{MENTIONS_MAP}}

        ### Output Format
        Return the final result **strictly** in the following XML format without any additional symbols such as triple backticks:
        <title>
        [Title of the discussion]
        </title>
        <description>
        [Brief description of the discussion (3-4 sentences)]
        </description>
        <summary>
        [List of key points summarizing the discussion]
        </summary>

        Do not add any extra formatting such as Markdown code blocks (```xml) at the beginning or end.
        Ensure that the <summary> section is properly closed with </summary>.
        The output must be valid XML. No extra characters, missing tags, or formatting issues.

        {{DISCUSSION}}
        """;
}

public record ConversationSummary(string Title, string Description, string Summary);

public record ConversationSummarizerResult
{
    public static readonly ConversationSummarizerResult Empty = new (new InvalidOperationException(), null);

    public ConversationSummarizerResult(ConversationSummary summary)
        => Summary = summary;

    public ConversationSummarizerResult(Exception exception, TimeSpan? postpone)
    {
        Exception = exception;
        Postpone = postpone;
    }

    public ConversationSummary? Summary { get; init; }
    public bool HasResult => Summary is not null;
    public Exception? Exception { get; init; }
    public TimeSpan? Postpone { get; init; }

    public static implicit operator ConversationSummarizerResult(ConversationSummary summary) => new(summary);
}

public class ConversationSummarizerStub: IConversationSummarizer
{
    public Task<ConversationSummarizerResult> Summarize(
        IReadOnlyCollection<TextEntry> chatEntries,
        CancellationToken cancellationToken)
    {
        var firstEntry = chatEntries.FirstOrDefault();
        var lastEntry = chatEntries.LastOrDefault();
        var count = chatEntries.Count;
        var summary = new ConversationSummary(
            $"Title {firstEntry!.LocalId} - {lastEntry!.LocalId}",
            $"Description {firstEntry.LocalId} - {lastEntry.LocalId}",
            $"Summary {count} entries"
        );
        return Task.FromResult<ConversationSummarizerResult>(summary);
    }
}
