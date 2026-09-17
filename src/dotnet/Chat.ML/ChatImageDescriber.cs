using ActualChat.AI;
using ActualLab.IO;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ActualChat.Chat.ML;

public interface IChatImageDescriber
{
    Task<string> Describe(
        string title,
        string description,
        IReadOnlyCollection<ChatEntrySlim> chatEntries,
        CancellationToken cancellationToken);
}

// Turns a chat into a description of a picture for it. The visual style is not this service's
// business - the image suggestion backend wraps whatever comes back in the house template, so what
// is wanted here is the subject only.

public class ChatImageDescriber(ChatImageDescriber.Options settings, IServiceProvider services)
    : IChatImageDescriber
{
    public class Options
    {
        public FilePath PromptFile { get; set; } = "";
    }

    public const string ServiceKey = ConversationSummarizer.ServiceKey;

    private readonly ChatDialogFormatterOptions _chatDialogFormatterOptions = new () {
        DisplayTimestamp = false,
        DisplayAuthorPerEntry = true,
        UseSquareBracketsFormat = true,
    };

    private Options Settings { get; } = settings;
    private Kernel Kernel => field ??= services.GetRequiredService<Kernel>();
    private IChatCompletionService ChatCompletionService
        => field ??= Kernel.GetRequiredService<IChatCompletionService>(ServiceKey);
    private IPromptHelpers PromptHelpers => field ??= services.GetRequiredService<IPromptHelpers>();
    private IChatDialogFormatter ChatDialogFormatter => field ??= services.GetRequiredService<IChatDialogFormatter>();
    private ILogger Log => field ??= services.LogFor(GetType());

    // Unlike the summarizers, this one is reached from an interactive banner rather than a flow,
    // so a prompt file the deployment forgot to ship must degrade instead of throwing at the UI.
    private string PromptTemplate {
        get {
            if (field != null)
                return field;

            try {
                return field = File.ReadAllText(Settings.PromptFile).Trim();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
                Log.LogError(e, "Image description prompt is missing: {PromptFile}", Settings.PromptFile);
                return field = "";
            }
        }
    }

    public async Task<string> Describe(
        string title,
        string description,
        IReadOnlyCollection<ChatEntrySlim> chatEntries,
        CancellationToken cancellationToken)
    {
        var promptTemplate = PromptTemplate;
        if (promptTemplate.IsNullOrEmpty())
            return ""; // No prompt, no description - the banner simply never appears

        var discussion = chatEntries.Count == 0
            ? ""
            : await ChatDialogFormatter.EntriesToText(chatEntries, _chatDialogFormatterOptions).ConfigureAwait(false);
        var prompt = PromptHelpers.BuildPrompt(
            promptTemplate,
            new Dictionary<string, string> {
                { "TITLE", title.Truncate(200) },
                { "DESCRIPTION", description.Truncate(2_000) },
                { "DISCUSSION", discussion.Truncate(10_000) },
            });
        string? reply;
        try {
            reply = await Ask(prompt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to describe an image for chat '{Title}'", title);
            return "";
        }
        if (reply.IsNullOrEmpty())
            return "";

        return PromptHelpers.GetXmlTagValue(reply, "image_description").Trim().NullIfEmpty() ?? "";
    }

    // Private methods

    private async Task<string?> Ask(string prompt, CancellationToken cancellationToken)
    {
        var response = await ChatCompletionService
            .GetChatMessageContentAsync(prompt, null, Kernel, cancellationToken)
            .ConfigureAwait(false);
        return response.Content;
    }
}

public class ChatImageDescriberStub : IChatImageDescriber
{
    public Task<string> Describe(
        string title,
        string description,
        IReadOnlyCollection<ChatEntrySlim> chatEntries,
        CancellationToken cancellationToken)
        => Task.FromResult("");
}
