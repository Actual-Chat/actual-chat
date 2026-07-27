using System.Net;
using ActualChat.Diagnostics;
using ActualLab.Diagnostics;
using ActualLab.IO;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using HttpOperationException = Microsoft.SemanticKernel.HttpOperationException;

namespace ActualChat.Chat.ML;

public interface IConversationSummarizer
{
    Task<ConversationSummarizerResult> Summarize(
        IReadOnlyCollection<ChatEntrySlim> chatEntries,
        CancellationToken cancellationToken);
}

public class ConversationSummarizer(ConversationSummarizer.Options settings, IServiceProvider services): IConversationSummarizer
{
    public class Options
    {
        public FilePath PromptFile { get; set; } = "";
    }

    public const string ServiceKey = nameof(ConversationSummarizer);
    internal const int MaxMentionCount = 20;
    internal const int MaxOutputLength = MaxTitleLength + MaxDescriptionLength + MaxSummaryLength;

    private const int MaxTitleLength = 200;
    private const int MaxDescriptionLength = 1_000;
    private const int MaxSummaryLength = 8_000;
    private static readonly MarkupParser SummaryMarkupParser = new();
    private readonly ChatDialogFormatterOptions _chatDialogFormatterOptions = new () {
        DisplayTimestamp = false,
        DisplayAuthorPerEntry = false,
        UseSquareBracketsFormat = true,
        AddAuthorId = true,
    };

    private Options Settings { get; } = settings;

    private IChatEntryLanguagesBackend ChatEntryLanguagesBackend => field ??= services.GetRequiredService<IChatEntryLanguagesBackend>();
    private Kernel Kernel => field ??= services.GetRequiredService<Kernel>();
    private IChatCompletionService Completion => field ??= Kernel.GetRequiredService<IChatCompletionService>(ServiceKey);
    private IChatDialogFormatter ChatDialogFormatter => field ??= services.GetRequiredService<IChatDialogFormatter>();
    private IAuthorNameRetriever AuthorNameRetriever => field ??= services.GetRequiredService<IAuthorNameRetriever>();
    private ILogger Log => field ??= services.LogFor(GetType());
    private string PromptTemplateString => field ??= File.ReadAllText(Settings.PromptFile).Trim();
    private IPromptTemplate PromptTemplate => field ??= BuildPromptTemplate();

    public async Task<ConversationSummarizerResult> Summarize(
        IReadOnlyCollection<ChatEntrySlim> chatEntries,
        CancellationToken cancellationToken)
    {
        if (PromptTemplateString.IsNullOrEmpty())
            throw StandardError.Constraint("Summarize conversation prompt is not configured.");

        using var activity = CoreServerInstruments.ActivitySource.StartActivity(GetType());

        var authorIds = chatEntries.Select(c => c.AuthorId).Distinct().ToArray();
        var chatId = authorIds.First().ChatId;
        var language = await GetMostCommonLanguage(chatId, chatEntries, cancellationToken).ConfigureAwait(false);
        var discussion = await ChatDialogFormatter.EntriesToText(chatEntries, _chatDialogFormatterOptions).ConfigureAwait(false);
        var authorMap = await BuildAuthorMap(authorIds).ConfigureAwait(false);
        var executionSettings = CreateExecutionSettings();
        var chatHistory = await BuildRequest(language, authorMap, discussion, cancellationToken).ConfigureAwait(false);
        string? result;
        try {
            var response = await Completion
                .GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings,
                    Kernel,
                    cancellationToken)
                .ConfigureAwait(false);
            result = response.Content;
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (HttpOperationException e) when (e.StatusCode == HttpStatusCode.TooManyRequests)  {
            activity?.Finalize(e, cancellationToken);
            Log.LogDebug(e, "Can't summarize. Rate limit exceeded");
            if (!TryExtractTryAgainInDelay(e.Message, out var postpone))
                postpone = TimeSpan.FromSeconds(45);
            return new ConversationSummarizerResult(e, postpone);
        }
        catch (Exception e) {
            activity?.Finalize(e, cancellationToken);
            Log.LogWarning(e, "Can't summarize. Unexpected error");
            throw;
        }
        var firstEntry = chatEntries.First();
        var lastEntry = chatEntries.Last();
        var count = chatEntries.Count;
        if (result.IsNullOrEmpty())
            return new ConversationSummary($"Summary of {count} entries with range: {firstEntry.LocalId} - {lastEntry.LocalId}",
                "",
                "Failed to retrieve summary");

        try {
            var json = result
                .Replace("```json", "", StringComparison.OrdinalIgnoreCase)
                .Replace("```", "")
                .Trim();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var title = root.TryGetProperty("title", out var t) ? t.GetString()?.Trim().NullIfEmpty() : null;
            var description = root.TryGetProperty("description", out var d) ? d.GetString()?.Trim().NullIfEmpty() : null;
            var summary = root.TryGetProperty("summary", out var s) ? s.GetString()?.Trim().NullIfEmpty() : null;

            title ??= $"Summary of {count} entries with range: {firstEntry.LocalId} - {lastEntry.LocalId}";
            description ??= "";
            summary ??= result;
            return SanitizeSummary(new ConversationSummary(title, description, summary), authorIds);
        }
        catch {
            return new ConversationSummary(
                $"Summary of {count} entries with range: {firstEntry.LocalId} - {lastEntry.LocalId}",
                "",
                "Failed to parse response");
        }
    }

    internal static ConversationSummary SanitizeSummary(
        ConversationSummary summary,
        IReadOnlyCollection<AuthorId> authorIds)
    {
        var allowedMentionIds = authorIds
            .Select(MentionRef.NewAuthor)
            .ToHashSet();
        var state = new SummaryMarkupState(allowedMentionIds);
        return new ConversationSummary(
            SanitizeMarkup(summary.Title, MaxTitleLength, state),
            SanitizeMarkup(summary.Description, MaxDescriptionLength, state),
            SanitizeMarkup(summary.Summary, MaxSummaryLength, state));
    }

    private async Task<string> BuildAuthorMap(AuthorId[] authorIds)
    {
        // Build a JSON object mapping:
        // "[Author Name|LocalId]" -> "@a:<chatId>:<localId>"
        var map = new Dictionary<string, string>();
        foreach (var authorId in authorIds) {
            var authorName = await AuthorNameRetriever.GetAuthorName(authorId).ConfigureAwait(false);
            var key = $"[{authorName}|{authorId.LocalId}]";
            var mentionId = MentionRef.NewAuthor(authorId);
            var value = "@" + mentionId.Value; // ensure leading '@' as required
            map[key] = value;
        }
        return JsonSerializer.Serialize(map);
    }

    private async ValueTask<ChatHistory> BuildRequest(
        Language? language,
        string authorMap,
        string conversation,
        CancellationToken cancellationToken)
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        var chatHistory = new ChatHistory();
        var arguments = new KernelArguments {
            { "TargetLanguage", $"{language?.Title ?? "same"}" },
        };
        var systemMessage = await PromptTemplate
            .RenderAsync(Kernel, arguments, cancellationToken)
            .ConfigureAwait(false);
        chatHistory.AddSystemMessage(systemMessage);

        sb.AppendLine("Author Mapping: ");
        sb.AppendLine(authorMap);
        sb.AppendLine();
        sb.AppendLine("Conversation:");
        sb.AppendLine(conversation);
        chatHistory.AddUserMessage(sb.ToStringAndRelease());
        return chatHistory;
    }

    public async Task<Language?> GetMostCommonLanguage(ChatId chatId, IReadOnlyCollection<ChatEntrySlim> chatEntries, CancellationToken cancellationToken)
    {
        if (chatEntries.Count == 0)
            return null;

        var localIds = chatEntries.Select(ce => ce.LocalId).ToList();
        // Determine the minimal tile range that covers all provided local IDs
        var minLid = localIds.Min();
        var maxLid = localIds.Max();
        var lidRange = new Range<long>(minLid, maxLid + 1);
        var lidTiles = Constants.Chat.ServerIdTileStack.FirstLayer.GetCoveringTiles(lidRange);

        var tiles = await lidTiles
            .Select(idTile => ChatEntryLanguagesBackend.GetTile(chatId, idTile.Range, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        // Build a fast lookup set of the requested local IDs
        var idSet = new HashSet<long>(localIds);

        // For each entry, take the primary detected language (first in the array)
        var frequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var entryLanguages = tiles
            .SelectMany(t => t.Entries)
            .Where(t => idSet.Contains(t.Id.LocalId));
        foreach (var entry in entryLanguages) {
            var lang = entry.Languages.FirstOrDefault();
            if (lang == null)
                continue;

            var id = lang.Value;
            if (id.IsNullOrEmpty())
                continue;

            frequency[id] = frequency.TryGetValue(id, out var count) ? count + 1 : 1;
        }

        if (frequency.Count == 0)
            return null;

        // Pick the most common; deterministic tie-breaker by id
        var best = frequency
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .First().Key;

        return Language.Parse(best);
    }

    private static bool TryExtractTryAgainInDelay(string message, out TimeSpan tryAgainInDelay)
    {
        tryAgainInDelay = TimeSpan.Zero;
        const string pleaseTryAgainIn = "Please try again in";
        var index1 = message.IndexOf(pleaseTryAgainIn);
        if (index1 < 0)
            return false;

        var index2 = message.IndexOf(". Visit", index1);
        if (index2 < 0)
            return false;

        var startIndex = index1 + pleaseTryAgainIn.Length;
        var sDelay = message.Substring(startIndex, index2 - startIndex).Trim();
        var index3 = sDelay.FirstIndexOf(char.IsLetter);
        if (index3 < 0)
            return false;

        var sValue = sDelay.Substring(0, index3);
        var units = sDelay.Substring(index3);
        if (!double.TryParse(sValue, out var value))
            return false;

        if (units == "ms")
            tryAgainInDelay = TimeSpan.FromMilliseconds(value);
        else if (units == "s")
            tryAgainInDelay = TimeSpan.FromSeconds(value);
        else
            tryAgainInDelay = TimeSpan.FromSeconds(55);

        return true;
    }

    private static PromptExecutionSettings CreateExecutionSettings()
        => new OpenAIPromptExecutionSettings {
            ReasoningEffort = null,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                JsonDocument.Parse(
                    """
                    {
                      "type": "object",
                      "properties": {
                        "title": { "type": "string" },
                        "description": { "type": "string" },
                        "summary": { "type": "string" }
                      }
                    }
                    """).RootElement,
                "conversation_summary",
                "The conversation summary with title, description, and summary fields"),
        };

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

    private static string SanitizeMarkup(string value, int maxLength, SummaryMarkupState state)
    {
        var markup = SummaryMarkupParser.Parse(value);
        markup = SummaryMarkupFilter.Instance.Filter(markup, state);
        markup = MarkupTrimmer.Instance.Trim(markup, maxLength, MentionMarkup.DefaultFormatter);
        return MarkupFormatter.Default.Format(markup).Truncate(maxLength);
    }

    // Nested types

    private sealed record SummaryMarkupFilter : MarkupRewriter<SummaryMarkupState>
    {
        public static readonly SummaryMarkupFilter Instance = new();

        public Markup Filter(Markup markup, SummaryMarkupState state)
        {
            var filterState = state;
            return Visit(markup, ref filterState);
        }

        protected override Markup VisitListItem(ListItemMarkup markup, ref SummaryMarkupState state)
        {
            var content = Visit(markup.Content, ref state);
            return content == markup.Content ? markup : new ListItemMarkup(content);
        }

        protected override Markup VisitMention(MentionMarkup markup, ref SummaryMarkupState state)
        {
            if (markup.Id.Kind != MentionKind.Author
                || !state.AllowedMentionIds.Contains(markup.Id)
                || state.MentionCount >= MaxMentionCount)
                return Markup.EmptyText;

            state.MentionCount++;
            return markup;
        }
    }

    private sealed class SummaryMarkupState(HashSet<MentionRef> allowedMentionIds)
    {
        public HashSet<MentionRef> AllowedMentionIds { get; } = allowedMentionIds;
        public int MentionCount { get; set; }
    }
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
        IReadOnlyCollection<ChatEntrySlim> chatEntries,
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
