namespace ActualChat.Core.UnitTests;

public sealed class TestWaitTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task PolledContainPredicateThatNeverHoldsShouldFail()
    {
        // arrange
        int[] items = [1];

        // act
        var wait = () => TestWait.WhenPolled(
            () => items.Should().Contain(x => x > 1, "no item ever matches"),
            Timeout, isExactTimeout: true);

        // assert
        await wait.Should().ThrowAsync<Exception>("a failed poll must not make the next one pass unchecked");
    }

    [Fact]
    public async Task PolledTaskContainPredicateThatNeverHoldsShouldFail()
    {
        // arrange
        int[] items = [1];

        // act
        var wait = () => TestWait.WhenPolled(
            () => {
                items.Should().Contain(x => x > 1, "no item ever matches");
                return Task.CompletedTask;
            },
            Timeout, isExactTimeout: true);

        // assert
        await wait.Should().ThrowAsync<Exception>("a failed poll must not make the next one pass unchecked");
    }
}
