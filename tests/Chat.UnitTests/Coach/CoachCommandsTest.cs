using ActualLab.Resilience;

namespace ActualChat.Chat.UnitTests.Coach;

public class CoachCommandsTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void AnalyzeEntryShouldOutliveTheDefaultQueueTimeout()
    {
        // arrange
        var command = new CoachAnalysisBackend_AnalyzeEntry(ChatEntryId.New(GroupChatId.New(), 1), false);

        // assert
        command.Should().BeAssignableTo<IHasTimeout>("the immediate path calls the LLM inside the command");
        ((IHasTimeout)command).Timeout.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(2));
    }
}
