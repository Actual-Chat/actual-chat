using ActualChat.MLSearch.Bot.Services;
using ActualChat.MLSearch.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ActualChat.MLSearch.IntegrationTests.Bot;

public class SearchTypeDetectorTest(ITestOutputHelper @out): TestBase(@out)
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

    public static TheoryData<string, SearchType> ExpectedPairs => new() {
        { "Search in public chats", SearchType.Public },
        { "search in all chats", SearchType.General },
        { "London is the capital of the Great Britain", SearchType.None },
        { "search in my chats", SearchType.Private },
        { "search in my private chats", SearchType.Private },
        { "search in my public chats", SearchType.Public },
        { "search in public and private chats", SearchType.General },
    };

    private readonly ISearchTypeDetector _searchTypeDetector = new SearchTypeDetector(CreateKernel());

    [Theory]
    [MemberData(nameof(ExpectedPairs))]
    public async Task SearchTypeDetectorProvidesExpectedOutput(string userInput, SearchType expectedSearchType)
    {
        var searchType = await _searchTypeDetector.Detect(new ChatMessageContent(AuthorRole.User, userInput));
        Assert.Equal(expectedSearchType, searchType);
    }
}
