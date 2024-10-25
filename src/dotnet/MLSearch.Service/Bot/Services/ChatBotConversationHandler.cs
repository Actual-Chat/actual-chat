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
    public const string Limit = nameof(Limit);
    public const string SearchType = nameof(SearchType);
    public const string ConversationId = nameof(ConversationId);
    public const string UserId = nameof(UserId);
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
    ICommander commander,
    IAuthorsBackend authors,
    ISearchTypeDetector searchTypeDetector,
    ISearchBotPluginSet searchBotPluginSet)
    : IBotConversationHandler
{
    private const string AgentInstructions =
    $$$"""
    Your name is {{{nameof(Constants.User.Sherlock)}}} and you are helpful content search assistant.

    As a search professional you have access to a variety of tools but your ultimate goal is to call
    {{{nameof(SearchPlugin)}}}-{{{nameof(SearchPlugin.Find)}}} tool (lets call it FIND) with proper arguments.
    You are supposed to use other tools and your expertise to extract the FIND tool dependencies from the user input.
    For the reference: User input is a history of your conversation with user.

    After getting the FIND tool results as a list of Text and Link pairs, please summarize Texts and
    respond with JSON in according to the following schema:

        {
            "summary": "your summary regarding the last search",
            "matches": "a comma separated list of Links to matched documents"
        }

    Use the values below when needed:
    - The search type is {{${{{nameof(SearchBotArguments.SearchType)}}}}}.
    - The limit on number of results returned is {{${{{nameof(SearchBotArguments.Limit)}}}}}.
    - An ID of the current search conversation is {{${{{nameof(SearchBotArguments.ConversationId)}}}}}.
    - An ID of the user who runs the search is {{${{{nameof(SearchBotArguments.UserId)}}}}}.
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
                new() { Name = SearchBotArguments.Limit, IsRequired = true }
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

        if (userMessages.Count==0)
            return;

        var searchType = default(SearchType?);
        while (userMessages.TryPop(out var message)) {
            chat.Add(message);
            var detectedSearchType = await searchTypeDetector.Detect(message, cancellationToken).ConfigureAwait(false);
            if (detectedSearchType != SearchType.None)
                searchType = detectedSearchType;
        }

        var lastAuthorId = updatedEntries[updatedEntries.Count-1].AuthorId;

        var author = await authors
            .Get(chatId, lastAuthorId, AuthorsBackend_GetAuthorOption.Full, cancellationToken)
            .ConfigureAwait(false);
        var userId = author!.UserId;

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        var arguments = new KernelArguments(executionSettings) {
            // Search everywhere by default
            { SearchBotArguments.ConversationId, chatId },
            { SearchBotArguments.UserId, userId },
            { SearchBotArguments.SearchType, searchType ?? SearchType.General },
            { SearchBotArguments.Limit, 5 },
        };

        // Invoke and display assistant response
        await foreach (var response in _agent.InvokeAsync(chat, arguments: arguments, cancellationToken: cancellationToken)) {
            chat.Add(response);
            await PostResponse(response).ConfigureAwait(false);
        }

        return;

        async Task PostResponse(ChatMessageContent message)
        {
            var textEntryId = new TextEntryId(chatId, 0, AssumeValid.Option);
            var upsertCommand = new ChatsBackend_ChangeEntry(
                textEntryId,
                null,
                Change.Create(new ChatEntryDiff {
                    AuthorId = botId,
                    Content = message.Content,
                }));
            await commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
        }
    }
}

#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

