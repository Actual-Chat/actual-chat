using System.Net;
using ActualChat.Integrations.Anthropic;
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
    private IPromptUtils PromptUtils => field ??= services.GetRequiredService<IPromptUtils>();
    [field: AllowNull, MaybeNull]
    private IAuthorNameRetriever AuthorNameRetriever => field ??= services.GetRequiredService<IAuthorNameRetriever>();
    [field: AllowNull, MaybeNull]
    private IChatDialogFormatter ChatDialogFormatter => field ??= services.GetRequiredService<IChatDialogFormatter>();

    // [field: AllowNull, MaybeNull]
    // private ChatSettings Settings => field ??= services.GetRequiredService<ChatSettings>();
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= services.LogFor(GetType());

    public async Task<ConversationSummarizerResult> Summarize(
        IReadOnlyCollection<TextEntry> chatEntries,
        CancellationToken cancellationToken)
    {
        var authorIds = chatEntries.Select(c => c.AuthorId)
            .Distinct()
            .ToArray();
        var authors = await authorIds
            .Select(c => AuthorNameRetriever.GetAuthorName(c))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

         var authorList = authors.SkipNullItems().Select(c => c).Order(StringComparer.Ordinal).ToCommaPhrase();
         var discussion = await ChatDialogFormatter.EntriesToText(chatEntries, _chatDialogFormatterOptions).ConfigureAwait(false);

         var prompt = PromptUtils.BuildPrompt(
            PromptTemplate,
            new Dictionary<string, string>(StringComparer.Ordinal) {
                { "AUTHORS", authorList.Substring(0, Math.Min(authorList.Length, 1_000)) },
                { "DISCUSSION", discussion.Substring(0, Math.Min(discussion.Length, 100_000)) },
            });

        string? reply = "";
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

        var title = PromptUtils.GetXmlTagValue(reply, "title").Trim().NullIfEmpty()
            ?? $"Summary of {count} entries with range: {firstEntry.LocalId} - {lastEntry.LocalId}";
        var description = PromptUtils.GetXmlTagValue(reply, "description").Trim().NullIfEmpty() ?? "";
        var summary = PromptUtils.GetXmlTagValue(reply, "summary").Trim().NullIfEmpty() ?? reply;

        return new ConversationSummary(title, description, summary);
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
        Summarize following text discussion of several people ({{AUTHORS}}) in several sentences.
        Specify what topics have been discussed and key moments.
        Who made commitments and what commitments are.
        Additionally:

        Provide a title that reflects the essence of the discussion.
        Give a brief description of the discussion (1-2 sentences).
        Provide all results in the language of the discussion.
        Present your final decision in the following xml format:

        <title>
        [A title that reflects the essence of the discussion.]
        </title>
        <description>
        [A brief description of the discussion (1-2 sentences).]
        </description>
        <summary>
        [A summary of the text discussion.]
        </summary>

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
        Exception = Exception;
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
