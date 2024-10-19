using ActualChat.Chat;
using ActualChat.MLSearch.Bot.Tools.Context;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.History;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;


namespace ActualChat.MLSearch.Bot;

#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

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
    IAuthorsBackend authors,
    IBotToolsContextHandler botToolsContextHandler)
    : IBotConversationHandler
{
    private const string AgentInstructions =
    """
        For the given objective, come up with a simple step by step plan.
        Use tools provided if neccessary.
        You have the following list of tools:
        {tools}

        In order to use a tool, you can use <tool></tool> and <tool_input></tool_input> tags.
        You will then get back a response in the form <observation></observation>

        This plan should involve individual tasks, that if executed correctly
        will yield the correct answer.
        Do not add any superfluous steps.
        When you are done, respond with a final answer between <final_answer></final_answer>
        Make sure that each step has all the information needed - do not skip steps.

        Previous conversation was:
        {chat_history}

        Objective: {input}

        Thoughts: {agent_scratchpad}
    """;

    private const int reducerMessageCount = 10;
    private const int reducerThresholdCount = 10;

    private static ChatCompletionAgent CreateAgent(Kernel kernel)
        => new() {
            Name = Constants.User.Sherlock.Name,
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

    private readonly ChatCompletionAgent _agent = CreateAgent(kernel);

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

        while (userMessages.TryPop(out var message)) {
            chat.Add(message);
        }

        // Invoke and display assistant response
        await foreach (var message in _agent.InvokeAsync(chat, cancellationToken: cancellationToken)) {
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

internal sealed class SearchToolPlugin
{

}

#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
