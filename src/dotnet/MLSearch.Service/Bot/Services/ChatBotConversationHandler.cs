using ActualChat.Chat;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.History;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;


namespace ActualChat.MLSearch.Bot.Services;

#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

internal static class SearchBotArguments
{
    public const string TopN = nameof(TopN);
    public const string SearchType = nameof(SearchType);
}

/**
    A handler for incoming messages from a user.
    This class responsibility is to take those requests and forward it to a bot.
    It should generate JWT tokens together with the forwarded requests to allow
    the bot use internal tools. It is also possible that this bot would reply
    in asynchronous mode. That means that it will use the JWT token provided to
    send messages to a user through the internal API.
**/
internal class ChatBotConversationHandler(
    Kernel kernel,
    ISearchTypeDetector searchTypeDetector,
    ISearchBotPluginSet searchBotPluginSet)
    : IBotConversationHandler
{
    private const string AgentInstructions =
    $$$"""
    Your name is {{{nameof(Constants.User.Sherlock)}}} and you are helpful content search assistant.

    As a search professional you have access to a variety of tools but your ultimate goal is to call
    {{{nameof(SearchToolPlugin)}}}-{{{nameof(SearchToolPlugin.Find)}}} tool (lets call it FIND) with proper arguments.
    You are supposed to use other tools and your expertise to extract the FIND tool dependencies from the user input.
    For the reference: User input is a history of your conversation with user.

    After getting the FIND tool results as a list of matched documents, please summarize and provide top {{${{{nameof(SearchBotArguments.TopN)}}}}} document IDs
    matching user needs the best.

    Use {{${{{nameof(SearchBotArguments.SearchType)}}}}} as a search type.

    Respond in JSON format with the following JSON schema:

        {
            "summary": "your summary regarding the last search",
            "matches": "a comma separated list of matched document IDs"
        }
    """;

    private const int reducerMessageCount = 10;
    private const int reducerThresholdCount = 10;

    private static ChatCompletionAgent CreateAgent(Kernel kernel, ISearchBotPluginSet searchBotPluginSet)
    {
        kernel.Plugins.AddRange(searchBotPluginSet.Plugins);

        return new(CreateTemplateConfig(), new KernelPromptTemplateFactory()) {
            Name = nameof(Constants.User.Sherlock),
            Instructions = AgentInstructions,
            Kernel = kernel,
            Arguments = new KernelArguments(new OpenAIPromptExecutionSettings() {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            }),
            HistoryReducer = new ChatHistorySummarizationReducer(
                kernel.GetRequiredService<IChatCompletionService>(),
                reducerMessageCount,
                reducerThresholdCount),
        };
    }

    private static PromptTemplateConfig CreateTemplateConfig()
    {
        return new PromptTemplateConfig(AgentInstructions) {
            InputVariables = [
                new() { Name = SearchBotArguments.TopN, IsRequired = true }
            ]
        };
    }

    private readonly ChatCompletionAgent _agent = CreateAgent(kernel, searchBotPluginSet);

    private readonly Dictionary<ChatId, ChatHistory> _history = [];

    public async Task ExecuteAsync(
        IReadOnlyList<ChatEntry>? updatedEntries,
        IReadOnlyCollection<ChatEntryId>? deletedEntries,
        CancellationToken cancellationToken = default)
    {
        if (updatedEntries == null || updatedEntries.Count == 0)
            return;

        var chatId = updatedEntries[0].ChatId;

        if (!_history.TryGetValue(chatId, out var chat)) {
            chat = [];
        }

        var botId = new AuthorId(chatId, Constants.User.Sherlock.AuthorLocalId, AssumeValid.Option);
        var userMessages = new Stack<ChatMessageContent>();
        for (var idx = updatedEntries.Count-1; idx >= 0; idx--) {
            var entry = updatedEntries[idx];
            if (entry.AuthorId == botId)
                break;
            if (entry.Kind != ChatEntryKind.Text)
                continue;
            userMessages.Push(new ChatMessageContent(AuthorRole.User, entry.Content));
        }

        var searchType = default(SearchType?);
        while (userMessages.TryPop(out var message)) {
            chat.Add(message);
            var detectedSearchType = await searchTypeDetector.Detect(message, cancellationToken).ConfigureAwait(false);
            if (detectedSearchType != SearchType.None)
                searchType = detectedSearchType;
        }

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        var arguments = new KernelArguments(executionSettings) {
            // Search everywhere by default
            { SearchBotArguments.TopN, 5 },
            { SearchBotArguments.SearchType, searchType ?? SearchType.General }
        };

        // Invoke and display assistant response
        await foreach (var message in _agent.InvokeAsync(chat, arguments: arguments, cancellationToken: cancellationToken)) {
            chat.Add(message);
            // TODO: identify final response and post it to the chat
        }

        // var lastUpdatedDocument = updatedDocuments.LastOrDefault();
        // if (lastUpdatedDocument == null)
        //     return;

        // var chatId = lastUpdatedDocument.ChatId;
        // var authorId = lastUpdatedDocument.AuthorId;
        // var botId = new AuthorId(chatId, Constants.User.Sherlock.AuthorLocalId, AssumeValid.Option);
        // if (authorId == botId)
        //     return;

        // var author = await authors
        //     .Get(chatId, authorId, AuthorsBackend_GetAuthorOption.Full, cancellationToken)
        //     .ConfigureAwait(false);
        // var userId = author!.UserId;

        // if (lastUpdatedDocument.Kind != ChatEntryKind.Text)
        //     // Can't react on anything besides text yet.
        //     return;

        // TODO: implement chat loop

    }
}

#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

