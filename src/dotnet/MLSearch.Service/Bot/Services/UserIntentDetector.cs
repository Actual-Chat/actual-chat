using System.Collections.Frozen;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;


namespace ActualChat.MLSearch.Bot.Services;

#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

internal interface IUserIntentDetector
{
    Task<UserIntent> Detect(ChatMessageContent message, CancellationToken cancellationToken = default);
}

internal class UserIntentDetector(Kernel kernel): IUserIntentDetector
{
    private const string DetectSearchTypePrompt =
    """
    As an expert in detecting user intent, you follow a clear process to identify the target action.
    Depending on your answer, the action will vary, so the answer is critical.
    There are four possible actions:

    - PUBLIC_SEARCH means search in the publicly available chats
    - PRIVATE_SEARCH means search in the chats where the user is a member or owner
    - GENERAL_SEARCH means search in all chats, both PUBLIC and PRIVATE
    - RESET means clear dialog context

    There is also one special value UNCERTAIN, when it is unclear from the user's message what action to take next.

    Instructions:
    - If the user requests to reset or start the search over, the action is RESET
    - If the user says "search all chats," or "search everywhere," or "search public and private chats," the action is GENERAL_SEARCH
    - If the user explicitly mentions "public chats", or "common chats" or similar, the action is PUBLIC_SEARCH
    - If the user refers to "private chats" or "my chats" or similar, the action is PRIVATE_SEARCH
    - In all other cases when the user's message is unrelated to the actions above, the action is UNCERTAIN

    Important:
    - Every user message in the list redefines the action unless the action is UNCERTAIN.
    - In the output please return a space separated string of user intents. For example "PUBLIC_SEARCH RESET".
    """;

    private static readonly FrozenDictionary<string, UserIntent> ResponseMap = new Dictionary<string, UserIntent> {
        { "PUBLIC_SEARCH", UserIntent.PublicSearch },
        { "PRIVATE_SEARCH", UserIntent.PrivateSearch },
        { "GENERAL_SEARCH", UserIntent.GeneralSearch },
        { "RESET", UserIntent.Reset },
    }.ToFrozenDictionary();

    public async Task<UserIntent> Detect(ChatMessageContent message, CancellationToken cancellationToken = default)
    {
        var agent = new ChatCompletionAgent() {
            Name = nameof(UserIntentDetector),
            Instructions = DetectSearchTypePrompt,
            Kernel = kernel,
        };

        var response = await agent.InvokeAsync([message], cancellationToken: cancellationToken)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);

        var content = response.Content ?? string.Empty;

        return content.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Aggregate(UserIntent.None, (result, token) => ResponseMap.TryGetValue(token, out var intent) ? result | intent : result);
    }
}

#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

