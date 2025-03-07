using ActualChat.Integrations.Anthropic;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ActualChat.Chat.ML;

public interface IConversationSummarizer
{
    Task<(string Title, string Description, string Summary)> Summarize(
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

    public async Task<(string Title, string Description, string Summary)> Summarize(
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

        var reply = await Ask(prompt, cancellationToken).ConfigureAwait(false);
        var firstEntry = chatEntries.First();
        var lastEntry = chatEntries.Last();
        var count = chatEntries.Count;
        if (reply.IsNullOrEmpty())
            return new ($"Summary of {count} entries with range: {firstEntry.LocalId} - {lastEntry.LocalId}",
                "",
                "Failed to retrieve summary");

        var title = PromptUtils.GetXmlTagValue(reply, "title").Trim().NullIfEmpty()
            ?? $"Summary of {count} entries with range: {firstEntry.LocalId} - {lastEntry.LocalId}";
        var description = PromptUtils.GetXmlTagValue(reply, "description").Trim().NullIfEmpty() ?? "";
        var summary = PromptUtils.GetXmlTagValue(reply, "summary").Trim().NullIfEmpty() ?? reply;

        return (title, description, summary);
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

public class ConversationSummarizerStub: IConversationSummarizer
{
    public Task<(string Title, string Description, string Summary)> Summarize(
        IReadOnlyCollection<TextEntry> chatEntries,
        CancellationToken cancellationToken)
    {
        var firstEntry = chatEntries.FirstOrDefault();
        var lastEntry = chatEntries.LastOrDefault();
        var count = chatEntries.Count;
        // TODO(AK): Implement proper summarization with NLP ChatGPT 4o-mini
        return Task.FromResult<(string Title, string Description, string Summary)>(new ($"Title {firstEntry!.LocalId} - {lastEntry!.LocalId}",
            $"Description {firstEntry.LocalId} - {lastEntry.LocalId}",
            $"Summary {count} entries"));
    }
}
