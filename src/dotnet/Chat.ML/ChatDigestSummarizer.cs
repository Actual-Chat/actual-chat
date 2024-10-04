using ActualChat.Integrations.Anthropic;

namespace ActualChat.Chat.ML;

public interface IChatDigestSummarizer
{
    Task<IReadOnlyCollection<string>> Summarize(
        IReadOnlyCollection<ChatEntry> chatEntries,
        CancellationToken cancellationToken);
}

internal class ChatDigestSummarizer(
    IChatDialogFormatter chatDialogFormatter,
    IPromptUtils promptUtils,
    IAnthropicClient anthropicClient,
    ILogger<ChatDigestSummarizer> log) : IChatDigestSummarizer
{
    public async Task<IReadOnlyCollection<string>> Summarize(
        IReadOnlyCollection<ChatEntry> chatEntries,
        CancellationToken cancellationToken)
    {
        var text = await chatDialogFormatter.EntriesToText(chatEntries).ConfigureAwait(false);
        var prompt = promptUtils.BuildPrompt(
            Prompt,
            new Dictionary<string, string>(StringComparer.Ordinal) {
                { "DOCUMENT", text.Substring(0, Math.Min(text.Length, 1_000_000)) },
            });
        try {
            var response = await anthropicClient.Execute(prompt, cancellationToken).ConfigureAwait(false);
            var summary = promptUtils.GetXmlTagValue(response, "summary");
            return summary.Split("\n", StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        catch (Exception ex) {
            log.LogError(ex, "Summarization error");
            return [];
        }
    }

    public const string Prompt =
        """
        You are tasked with summarizing a document into a maximum of 10 bullet points. Here is the document you need to summarize:

        <document>
        {{DOCUMENT}}
        </document>

        To create an effective summary, follow these steps:

        1. Carefully read through the entire document to understand its main ideas and key points.

        2. Identify the most important information, focusing on main concepts, crucial details, and significant conclusions.

        3. Condense this information into clear, concise bullet points. Each bullet point should capture a single main idea or key piece of information.

        4. Limit your summary to a maximum of 10 bullet points. If the document is short or simple, you may use fewer bullet points, but never exceed 10.

        5. Ensure that your bullet points are:
           - Concise: Keep each point brief and to the point.
           - Self-contained: Each bullet should make sense on its own.
           - Informative: Provide substantive information, not vague statements.
           - Ordered logically: Arrange the points in a way that makes sense (e.g., chronologically, by importance, or following the structure of the original document).

        6. Use your judgment to determine the appropriate number of bullet points based on the length and complexity of the document. For shorter or simpler documents, fewer bullet points may suffice.

        7. Avoid repetition. Each bullet point should contribute unique information to the summary.

        8. Use clear, straightforward language. Avoid jargon unless it's essential to understanding the main points.

        9. If the document contains numerical data or statistics that are central to its message, include the most significant figures in your bullet points.

        10. After creating your summary, review it to ensure it accurately represents the main ideas of the original document without any significant omissions.

        Present your summary within <summary> tags, with each bullet point on a new line, preceded by a dash and a space. For example:

        <summary>
        - First main point
        - Second main point
        - Third main point
        </summary>

        Remember, your goal is to create a concise yet comprehensive summary that captures the essence of the document in no more than 10 bullet points.
        """;
}
