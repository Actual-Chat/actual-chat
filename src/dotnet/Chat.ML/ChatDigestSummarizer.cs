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

    Task<string?> SummarizeMediaShares(
        IReadOnlyCollection<ChatEntry> mediaEntries,
        Language language,
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
    private IAuthorNameRetriever AuthorNameRetriever => field ??= services.GetRequiredService<IAuthorNameRetriever>();
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
            new Dictionary<string, string>() {
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

    public async Task<string?> SummarizeMediaShares(
        IReadOnlyCollection<ChatEntry> mediaEntries,
        Language language,
        CancellationToken cancellationToken)
    {
        if (mediaEntries.Count == 0)
            return null;

        var shares = await BuildShares(mediaEntries).ConfigureAwait(false);
        if (shares.Count == 0)
            return null;

        var sharesText = string.Join("\n", shares.Select(FormatShareLine));
        var prompt = $"""
            You are summarizing media-only activity in a chat (no text messages were sent).

            Below is who shared what during the period:
            {sharesText}

            Write a one-line summary in {language.Title} describing what was shared and by whom.
            Use the authors' names verbatim. Wrap the result in <summary>...</summary> tags.
            """;
        try {
            var response = await Ask(prompt, cancellationToken).ConfigureAwait(false);
            var summary = PromptHelpers.GetXmlTagValue(response, "summary").Trim();
            return summary.IsNullOrEmpty() ? null : summary;
        }
        catch (Exception ex) {
            Log.LogError(ex, "Media-shares summarization error");
            return null;
        }
    }

    private async Task<List<Share>> BuildShares(IReadOnlyCollection<ChatEntry> mediaEntries)
    {
        var perAuthor = new Dictionary<AuthorId, Share>();
        foreach (var entry in mediaEntries) {
            if (!perAuthor.TryGetValue(entry.AuthorId, out var share))
                share = new Share(await AuthorNameRetriever.GetAuthorName(entry.AuthorId).ConfigureAwait(false));
            foreach (var a in entry.Attachments)
                if (a.IsSupportedImage())
                    share.Images++;
                else if (a.IsSupportedVideo())
                    share.Videos++;
                else
                    share.FileNames.Add(a.Media.FileName);
            perAuthor[entry.AuthorId] = share;
        }
        return perAuthor.Values.Where(s => s.Images + s.Videos + s.FileNames.Count > 0).ToList();
    }

    private static string FormatShareLine(Share s)
    {
        var parts = new List<string>(3);
        if (s.Images > 0)
            parts.Add(s.Images == 1 ? "an image" : $"{s.Images} images");
        if (s.Videos > 0)
            parts.Add(s.Videos == 1 ? "a video" : $"{s.Videos} videos");
        if (s.FileNames.Count == 1)
            parts.Add(s.FileNames[0]);
        else if (s.FileNames.Count > 1)
            parts.Add($"{s.FileNames.Count} files");
        return $"- {s.AuthorName}: {string.Join(", ", parts)}";
    }

    private async Task<string> Ask(string prompt, CancellationToken cancellationToken)
    {
        var response = await ChatCompletionService
            .GetChatMessageContentAsync(prompt, null, Kernel, cancellationToken)
            .ConfigureAwait(false);
        return response.Content ?? "";
    }

    private sealed class Share(string authorName)
    {
        public string AuthorName { get; } = authorName;
        public int Images { get; set; }
        public int Videos { get; set; }
        public List<string> FileNames { get; } = new();
    }
}

public class ChatDigestSummarizerStub : IChatDigestSummarizer
{
    public Task<IReadOnlyCollection<string>> Summarize(IReadOnlyCollection<ChatEntry> chatEntries, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyCollection<string>>([]);

    public Task<string?> SummarizeMediaShares(IReadOnlyCollection<ChatEntry> mediaEntries, Language language, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);
}
