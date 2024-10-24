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
        [Description("Perform a search for content related to the specified query")]
        public Task<string[]> Find(
            [Description("Type of the search to run")] SearchType searchType,
            [Description("What to search for")] string query
        )
        {
            CallCount += 1;
            var results = new string[] {
                $"Dumb {query} content",
                $"Expected {searchType} cotent"
            };
            return Task.FromResult(results);
        }
    }

    [Fact]
    public async Task ChatBotConversationHandlerCallsTools()
    {
        // Setup
        var searchTypeDetector = new Mock<ISearchTypeDetector>();
        searchTypeDetector.Setup(x => x.Detect(It.IsAny<ChatMessageContent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(SearchType.General));

        var mockSearchPlugin = new TestSearchToolPlugin();
        var searchBotPluginSet = new Mock<ISearchBotPluginSet>();
        searchBotPluginSet.SetupGet(x => x.Plugins).Returns([
            KernelPluginFactory.CreateFromObject(mockSearchPlugin, nameof(SearchToolPlugin)),
        ]);

        var conversationHandler = new ChatBotConversationHandler(
            CreateKernel(),
            searchTypeDetector.Object,
            searchBotPluginSet.Object);

        // Act
        string[] userMessages = [
            "Hi",
            "Search for transport infrastructure in my chats"
        ];
        var cancellationSource = new CancellationTokenSource();
        await conversationHandler.ExecuteAsync(ConvertToEntries(userMessages), [], cancellationSource.Token);

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

    private IReadOnlyList<ChatEntry> ConvertToEntries(IEnumerable<string> messages)
    {
        var chatId = new ChatId(Generate.Option);
        var localId = 1L;
        var version = DateTime.Now.Ticks;
        var entries = new List<ChatEntry>();
        foreach (var msg in messages) {
            var entryId = new ChatEntryId(chatId, ChatEntryKind.Text, localId++, AssumeValid.Option);
            entries.Add(new ChatEntry(entryId, version++) {
                Content = msg,
            });
        }
        return entries;
    }
}
