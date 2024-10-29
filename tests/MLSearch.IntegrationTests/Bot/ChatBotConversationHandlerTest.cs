using System.ComponentModel;
using ActualChat.Chat;
using ActualChat.MLSearch.Bot.Services;
using ActualChat.MLSearch.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ActualChat.MLSearch.IntegrationTests.Bot;

public class ChatBotConversationHandlerTest(ITestOutputHelper @out): TestBase(@out)
{
    [Fact]
    public async Task ChatBotConversationHandlerCallsTools()
    {
        // Setup
        var chatId = new ChatId(Generate.Option);
        var authorId = new AuthorId(chatId, 111, AssumeValid.Option);
        var userId = new UserId("TestUser", AssumeValid.Option);

        var commander = MockCommander();

        var authors = new Mock<IAuthorsBackend>();
        authors.Setup(x => x
            .Get(
                It.IsAny<ChatId>(),
                It.IsAny<AuthorId>(),
                It.IsAny<AuthorsBackend_GetAuthorOption>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<AuthorFull?>(new AuthorFull(authorId) {
                UserId = userId,
            }));

        var chatHistoryCache = new Mock<IChatHistoryCache>();
        chatHistoryCache.Setup(x => x.GetOrSetDefault(It.IsAny<ChatId>(),It.IsAny<ChatHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<ChatHistory>([]));

        var searchTypeDetector = new Mock<ISearchTypeDetector>();
        searchTypeDetector.Setup(x => x.Detect(It.IsAny<ChatMessageContent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(SearchType.General));

        var mockSearchPlugin = new Mock<ISearchPlugin>();
        mockSearchPlugin
            .Setup(x => x.Find(
                It.IsAny<string>(),
                It.IsAny<SearchType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, SearchType, string, string, int, CancellationToken>(
                (query, searchType, conversationId, userId, limit, cancellationToken)
                    => Task.FromResult<SearchResult[]>([
                        new SearchResult { Text = $"Dumb {query} content", Link = "link1" },
                        new SearchResult { Text = $"Expected {searchType} cotent", Link = "link2" },
                    ])
            );
        var forwardPlugin = Mock.Of<IForwardPlugin>();

        var searchBotPluginSet = new SearchBotPluginSet(mockSearchPlugin.Object, forwardPlugin);

        var conversationHandler = new ChatBotConversationHandler(
            CreateKernel(),
            commander.Object,
            authors.Object,
            chatHistoryCache.Object,
            searchTypeDetector.Object,
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
                It.Is<string>(x => x == "transport infrastructure"),
                It.Is<SearchType>(x => x == SearchType.General),
                It.Is<string>(x => x == chatId),
                It.Is<string>(x => x == userId),
                It.Is<int>(x => x == 5),
                It.Is<CancellationToken>(x => x == cancellationSource.Token)),
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
                modelId: openAISettings!.ChatModel)
            .Build();
    }

    private IReadOnlyList<ChatEntry> ConvertToEntries(AuthorId authorId, IEnumerable<string> messages)
    {
        var localId = 1L;
        var version = DateTime.Now.Ticks;
        var entries = new List<ChatEntry>();
        foreach (var msg in messages) {
            var entryId = new ChatEntryId(authorId.ChatId, ChatEntryKind.Text, localId++, AssumeValid.Option);
            entries.Add(new ChatEntry(entryId, version++) {
                Content = msg,
                AuthorId = authorId,
            });
        }
        return entries;
    }

    private static Mock<ICommander> MockCommander(Func<CommandContext, CancellationToken, Task>? action = null)
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory
            .Setup(x => x.CreateScope())
            .Returns(Mock.Of<IServiceScope>());
        var services = new Mock<IServiceProvider>();
        services
            .Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(scopeFactory.Object);
        var commander = new Mock<ICommander>();
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
