using ActualChat.Chat.ML;

namespace ActualChat.Chat.UnitTests;

public class OpenAIModelsTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData("gpt-5.6-terra", "none")]
    [InlineData("gpt-5.6-luna", "none")]
    [InlineData("gpt-5.1", "none")]
    [InlineData("gpt-5", "minimal")]
    [InlineData("gpt-5-mini", "minimal")]
    [InlineData("o4-mini", "low")]
    [InlineData("o3", "low")]
    [InlineData("gpt-4.1", null)]
    [InlineData("gpt-4.1-nano", null)]
    [InlineData("gpt-4o", null)]
    [InlineData("gpt-5-chat-latest", null)]
    [InlineData("gpt-5.1-chat-latest", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void LowestReasoningEffortShouldBeOneTheModelAccepts(string? modelId, string? expectedWireValue)
    {
        // act
        var wireValue = OpenAIModels.GetLowestReasoningEffort(modelId)?.ToString();

        // assert
        wireValue.Should().Be(expectedWireValue,
            "OpenAI answers HTTP 400 to a reasoning_effort the model doesn't support, and gpt-4.x accepts no value");
    }
}
