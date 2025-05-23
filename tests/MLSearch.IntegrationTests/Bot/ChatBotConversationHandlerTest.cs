using ActualChat.Chat;
using ActualChat.MLSearch.Bot.Services;
using ActualChat.MLSearch.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ActualChat.MLSearch.IntegrationTests.Bot;

public class ChatBotConversationHandlerTest(ITestOutputHelper @out): TestBase(@out)
{
    [Fact(Skip = "Requires OpenAI connection. Run explicitly.")]
    public async Task ChatBotConversationHandlerCallsTools()
    {
        // Setup
        var chatId = GroupChatId.New();
        var authorId = AuthorId.New(chatId, 111);
        var userId = UserId.Parse("TestUser");

        var commander = MockCommander();

        var authors = new Mock<IAuthorsBackend>(MockBehavior.Loose);
        authors.Setup(x => x
            .Get(
                It.IsAny<ChatId>(),
                It.IsAny<AuthorId>(),
                It.IsAny<RequestedAuthorKind>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<AuthorFull?>(new AuthorFull(userId, authorId)));

        var chatHistoryCache = new Mock<IChatHistoryCache>(MockBehavior.Loose);
        chatHistoryCache.Setup(x => x.GetOrSetDefault(It.IsAny<ChatId>(),It.IsAny<ChatHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<ChatHistory>([]));

        var userIntentDetector = new Mock<IUserIntentDetector>(MockBehavior.Loose);
        userIntentDetector.Setup(x => x.Detect(It.IsAny<ChatMessageContent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(UserIntent.GeneralSearch));

        var mockSearchPlugin = new Mock<ISearchPlugin>(MockBehavior.Loose);
        mockSearchPlugin
            .Setup(x => x.Find(
                It.IsAny<string>(),
                It.IsAny<SearchType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, SearchType, string, string, int, CancellationToken>(
                (query, searchType, conversationId, userId1, limit, cancellationToken)
                    => Task.FromResult<SearchResult[]>([
                        new SearchResult { Text = $"Dumb {query} content", Link = "link1" },
                        new SearchResult { Text = $"Expected {searchType} content", Link = "link2" },
                    ])
            );
        var forwardPlugin = Mock.Of<IForwardPlugin>(MockBehavior.Loose);

        var searchBotPluginSet = new SearchBotPluginSet(mockSearchPlugin.Object, forwardPlugin);

        var conversationHandler = new ChatBotConversationHandler(
            CreateKernel(),
            commander.Object,
            authors.Object,
            chatHistoryCache.Object,
            userIntentDetector.Object,
            searchBotPluginSet);

        // Act
        string[] userMessages = [
            "Hi",
            "Search for transport infrastructure in my chats"
        ];
        var cancellationSource = new CancellationTokenSource();
        await conversationHandler.ExecuteAsync(ConvertToEntries(authorId, userMessages), [], cancellationSource.Token);

        // Assert
        mockSearchPlugin.Verify(
            x => x.Find(
                It.Is<string>(c => c == "transport infrastructure"),
                It.Is<SearchType>(c => c == SearchType.General),
                It.Is<string>(c => c == chatId.Value),
                It.Is<string>(c => c == userId.Value),
                It.Is<int>(c => c == 5),
                It.Is<CancellationToken>(c => c == cancellationSource.Token)),
            Times.Once
        );
    }

    private static Kernel CreateKernel()
    {
        var configuration = GetConfiguration();

        var openAISettings = configuration.GetSection("MLSearchSettings:Bot:OpenAI").Get<OpenAISettings>();

        return Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(
                apiKey: openAISettings!.ApiKey,
                modelId: openAISettings.ChatModel)
            .Build();
    }

    private IReadOnlyList<ChatEntry> ConvertToEntries(AuthorId authorId, IEnumerable<string> messages)
    {
        var localId = 1L;
        var version = DateTime.Now.Ticks;
        var entries = new List<ChatEntry>();
        foreach (var msg in messages) {
            var entryId = TextEntryId.New(authorId.ChatId, localId++);
            entries.Add(new ChatEntry(entryId, version++) {
                Content = msg,
                AuthorId = authorId,
            });
        }
        return entries;
    }

    private static Mock<ICommander> MockCommander(Func<CommandContext, CancellationToken, Task>? action = null)
    {
        var scopeFactory = new Mock<IServiceScopeFactory>(MockBehavior.Loose);
        scopeFactory
            .Setup(x => x.CreateScope())
            .Returns(Mock.Of<IServiceScope>(MockBehavior.Loose));
        var services = new Mock<IServiceProvider>(MockBehavior.Loose);
        services
            .Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(scopeFactory.Object);
        var commander = new Mock<ICommander>(MockBehavior.Loose);
        commander
            .SetupGet(x => x.Services)
            .Returns(services.Object);
        commander
            .Setup(x => x.Run(
                It.IsAny<CommandContext>(),
                It.IsAny<CancellationToken>()))
            .Returns<CommandContext, CancellationToken>(
                (context, ct) => {
                    context.TryComplete(ct);
                    return action?.Invoke(context, ct) ?? Task.CompletedTask;
                });
        return commander;
    }
}
