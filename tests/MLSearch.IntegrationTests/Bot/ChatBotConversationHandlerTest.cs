using ActualChat.Chat;
using ActualChat.MLSearch.Bot.Services;
using ActualChat.MLSearch.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace ActualChat.MLSearch.IntegrationTests.Bot;

public class ChatBotConversationHandlerTest(ITestOutputHelper @out): TestBase(@out)
{
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

    [Fact]
    public async Task ChatBotConversationHandlerCallsTools()
    {
        var searchTypeDetector = new Mock<ISearchTypeDetector>();
        searchTypeDetector.Setup(x => x.Detect(It.IsAny<ChatMessageContent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(SearchType.General));

        var conversationHandler = new ChatBotConversationHandler(CreateKernel(), searchTypeDetector.Object);

        string[] userMessages = [
            "Hi",
            "Search for transport infrastructure in my chats"
        ];
        var cancellationSource = new CancellationTokenSource();
        await conversationHandler.ExecuteAsync(ConvertToEntries(userMessages), [], cancellationSource.Token);

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
