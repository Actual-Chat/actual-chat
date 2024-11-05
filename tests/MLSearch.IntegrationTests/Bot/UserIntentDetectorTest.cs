using ActualChat.MLSearch.Bot.Services;
using ActualChat.MLSearch.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ActualChat.MLSearch.IntegrationTests.Bot;

public class UserIntentDetectorTest(ITestOutputHelper @out): TestBase(@out)
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

    public static TheoryData<string, UserIntent> ExpectedPairs => new() {
        { "Search in public chats", UserIntent.PublicSearch },
        { "search in all chats", UserIntent.GeneralSearch },
        { "London is the capital of the Great Britain", UserIntent.None },
        { "search in my chats", UserIntent.PrivateSearch },
        { "search in my private chats", UserIntent.PrivateSearch },
        { "search in my public chats", UserIntent.PublicSearch },
        { "search in public and private chats", UserIntent.GeneralSearch },
        { "Lets start over", UserIntent.Reset },
        { "Please reset and search in public and private chats", UserIntent.GeneralSearch | UserIntent.Reset },
    };

    private readonly IUserIntentDetector _userIntentDetector = new UserIntentDetector(CreateKernel());

    [Theory(Skip = "Requires OpenAI connection. Run explicitly.")]
    [MemberData(nameof(ExpectedPairs))]
    public async Task SearchTypeDetectorProvidesExpectedOutput(string userInput, UserIntent expectedSearchType)
    {
        var searchType = await _userIntentDetector.Detect(new ChatMessageContent(AuthorRole.User, userInput));
        Assert.Equal(expectedSearchType, searchType);
    }
}
