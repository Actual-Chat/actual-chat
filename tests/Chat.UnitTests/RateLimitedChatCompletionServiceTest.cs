using ActualChat.Chat.ML;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Services;

namespace ActualChat.Chat.UnitTests;

public class RateLimitedChatCompletionServiceTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ModelIdShouldComeFromWrappedService()
    {
        // arrange
        var wrapped = new OpenAIChatCompletionService("gpt-4.1", "sk-test");
        var service = new RateLimitedChatCompletionService(wrapped, null!, "rate_limit:test");

        // act
        var modelId = service.GetModelId();

        // assert
        modelId.Should().Be("gpt-4.1",
            "the OpenAI call sites pick reasoning_effort by the model id they read through this wrapper");
    }
}
