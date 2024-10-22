using System.Collections.Frozen;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;


namespace ActualChat.MLSearch.Bot.Services;

#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

internal interface ISearchTypeDetector
{
    Task<SearchType> Detect(ChatMessageContent message, CancellationToken cancellationToken = default);
}

internal class SearchTypeDetector(Kernel kernel): ISearchTypeDetector
{
    private const string DetectSearchTypePrompt =
    """
    As an expert in searching for information in chats, you follow a clear process to identify the target search area.
    Depending on your answer, the search process runs through different subsets of chats, so the answer is critical.
    There are three possible search areas:
    - PUBLIC means search in the publicly available chats
    - PRIVATE means search in the chats where the user is a member or owner
    - GENERAL means search in all chats, both PUBLIC and PRIVATE
    There is also one special value UNCERTAIN, when it is unclear from the user's message where to run next search.
    Instructions:
    - If the user says "search all chats" or "search everywhere," the search area is GENERAL
    - If the user requested to reset or start the search over, the search area is GENERAL
    - If the user explicitly mentions "public chats" or similar, the search area is PUBLIC
    - If the user refers to "private chats" or "my chats" or similar, the search area is PRIVATE
    - In all other cases when user's message is unrelated to chats the search area is UNCERTAIN
    Important:
    - Every user message in the list redefines search area unless search area is UNCERTAIN.
    - Return only one word in the output (PUBLIC, PRIVATE, GENERAL or UNCERTAIN).
    """;

    private static readonly FrozenDictionary<string, SearchType> ResponseMap = new Dictionary<string, SearchType>() {
        { "PUBLIC", SearchType.Public },
        { "PRIVATE", SearchType.Private },
        { "GENERAL", SearchType.General },
    }.ToFrozenDictionary();

    public async Task<SearchType> Detect(ChatMessageContent message, CancellationToken cancellationToken = default)
    {
        var agent = new ChatCompletionAgent() {
            Name = "Detector",
            Instructions = DetectSearchTypePrompt,
            Kernel = kernel,
        };

        var response = await agent.InvokeAsync([message], cancellationToken: cancellationToken)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);


        return response != null && ResponseMap.TryGetValue(response.Content ?? string.Empty, out var searchType)
            ? searchType
            : SearchType.None;
    }
}

#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

