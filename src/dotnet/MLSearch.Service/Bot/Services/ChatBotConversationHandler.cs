using ActualChat.Chat;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
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

internal class ChatBotConversationHandler(
    Kernel kernel,
    ICommander commander,
    IAuthorsBackend authors,
    IChatHistoryCache chatHistoryCache,
    IUserIntentDetector userIntentDetector,
    SearchBotPluginSet searchBotPluginSet)
    : IBotConversationHandler
{
    private const string AgentInstructions =
    $$$"""
    Your name is {{{nameof(Constants.User.Sherlock)}}} and you are helpful content search assistant.

    As a search professional you have access to a variety of tools in your toolbox.
    But among others the tools below are critical to you mission.
    - The FIND tool which full name is {{{PluginNames.SearchPlugin}}}-{{{nameof(ISearchPlugin.Find)}}}
        allows you to retrieve relevant content.
    - The FORWARD tool which full name is {{{PluginNames.ForwardPlugin}}}-{{{nameof(IForwardPlugin.ForwardResults)}}}
        allows you forwarding relevant results to the user.

    In the very beginning you should decide whether it is a search request or user just tries to communicate.

    When user greets you or asks for something not related to searching information in chats you should
    briefly respond to that message with an information what is your primary goal asking about a
    relevant input.

    If user requests for reset search or start over please mention you understand their intent.

    In the case user asks for search, your first objective is to call FIND tool with proper arguments
    and you are supposed extracting those from the conversation history.
    Once you have the FIND tool results as a list of Text and Link pairs
    you second goal is to forward those results to the user. Please summarize found Texts and pass
    that summary along with a list of Links to the FORWARD tool.
    Your final message should be a concise report you completed the search and ready for the next questions.
    Please briefly appologise if search results are empty and ask user to change search parameters.
    IMPORTANT!: You always have access to either public or private chats of the current user, so don't
    hesitate calling FIND tool every time user asks for search.

    Use the values below when needed:
    - The search type is {{${{{nameof(SearchBotArguments.SearchType)}}}}}.
    - The limit on number of results returned is {{${{{nameof(SearchBotArguments.Limit)}}}}}.
    - An ID of the current search conversation is {{${{{nameof(SearchBotArguments.ConversationId)}}}}}.
    - An ID of the user who runs the search is {{${{{nameof(SearchBotArguments.UserId)}}}}}.
    """;

    private const int reducerMessageCount = 10;
    private const int reducerThresholdCount = 10;

    private static ChatCompletionAgent CreateAgent(Kernel kernel, SearchBotPluginSet searchBotPluginSet)
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
        => new PromptTemplateConfig(AgentInstructions) {
            InputVariables = [
                new() { Name = SearchBotArguments.ConversationId, IsRequired = true },
                new() { Name = SearchBotArguments.UserId, IsRequired = true },
                new() { Name = SearchBotArguments.SearchType, IsRequired = true },
                new() { Name = SearchBotArguments.Limit, IsRequired = true },
            ]
        };

    private readonly ChatCompletionAgent _agent = CreateAgent(kernel, searchBotPluginSet);

    public async Task ExecuteAsync(
        IReadOnlyList<ChatEntry>? updatedEntries,
        IReadOnlyCollection<ChatEntryId>? deletedEntries,
        CancellationToken cancellationToken = default)
    {
        if (updatedEntries == null || updatedEntries.Count == 0)
            return;

        var chatId = updatedEntries[0].ChatId;

        var chat = await chatHistoryCache.GetOrSetDefault(chatId, [], cancellationToken).ConfigureAwait(false);

        var botId = Constants.User.Sherlock.GetSherlockAuthorId(chatId);
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
            var userIntent = await userIntentDetector.Detect(message, cancellationToken).ConfigureAwait(false);
            if (userIntent.IsReset()) {
                searchType = default;
                chat.Clear();
            }
            if (userIntent.IsSearchType(out var requestedSearchType))
                searchType = requestedSearchType;

            chat.Add(message);
        }

        var lastAuthorId = updatedEntries[updatedEntries.Count-1].AuthorId;

        var author = await authors
            .Get(chatId, lastAuthorId, RequestedAuthorKind.Full, cancellationToken)
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
        var responseItems = _agent.InvokeAsync(new ChatHistoryAgentThread(chat),
            new AgentInvokeOptions { KernelArguments = arguments },
            cancellationToken);
        await foreach (var response in responseItems.ConfigureAwait(false)) {
            chat.Add(response);
            await PostResponse(response).ConfigureAwait(false);
        }

        await chatHistoryCache.Set(chatId, chat, cancellationToken).ConfigureAwait(false);

        return;

        async Task PostResponse(ChatMessageContent message)
        {
            var textEntryId = TextEntryId.New(chatId, 0);
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
