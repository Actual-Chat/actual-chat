using System.ComponentModel;
using ActualChat.Chat;
using ActualChat.MLSearch.Bot.Services;
using ActualChat.MLSearch.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace ActualChat.MLSearch.IntegrationTests.Bot;

public class ChatBotConversationHandlerTest(ITestOutputHelper @out): TestBase(@out)
{
    internal sealed class TestSearchToolPlugin
    {
        public int CallCount { get; private set; } = 0;

        [KernelFunction]
        [Description("Performs search for a content.")]
        public Task<SearchResult[]> Find(
            [Description("What to search for.")] string query,
            [Description("Type of the search to run.")] SearchType searchType,
            [Description("ID of ongoing search conversation.")] string conversationId,
            [Description("ID of the user who runs the search.")] string userId,
            [Description("Limit to the number of returned results.")] int? limit = 1
        )
        {
            CallCount += 1;
            var results = new [] {
                new SearchResult { Text = $"Dumb {query} content", Link = "link1" },
                new SearchResult { Text = $"Expected {searchType} cotent", Link = "link2" },
            };
            return Task.FromResult(results);
        }
    }

    [Fact]
    public async Task ChatBotConversationHandlerCallsTools()
    {
        // Setup
        var chatId = new ChatId(Generate.Option);
        var authorId = new AuthorId(chatId, 111, AssumeValid.Option);
        var userId = new UserId("TestUser", AssumeValid.Option);
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

        var searchTypeDetector = new Mock<ISearchTypeDetector>();
        searchTypeDetector.Setup(x => x.Detect(It.IsAny<ChatMessageContent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(SearchType.General));

        var mockSearchPlugin = new TestSearchToolPlugin();
        var searchBotPluginSet = new Mock<ISearchBotPluginSet>();
        searchBotPluginSet.SetupGet(x => x.Plugins).Returns([
            KernelPluginFactory.CreateFromObject(mockSearchPlugin, nameof(SearchPlugin)),
        ]);

        var conversationHandler = new ChatBotConversationHandler(
            CreateKernel(),
            authors.Object,
            searchTypeDetector.Object,
            searchBotPluginSet.Object);

        // Act
        string[] userMessages = [
            "Hi",
            "Search for transport infrastructure in my chats"
        ];
        var cancellationSource = new CancellationTokenSource();
        await conversationHandler.ExecuteAsync(ConvertToEntries(authorId, userMessages), [], cancellationSource.Token);

        // Assert
        Assert.Equal(1, mockSearchPlugin.CallCount);
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
}
