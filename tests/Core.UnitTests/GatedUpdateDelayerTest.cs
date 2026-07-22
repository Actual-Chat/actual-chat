namespace ActualChat.Core.UnitTests;

public class GatedUpdateDelayerTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task DelayCompletesOnlyAfterGateOpens()
    {
        // arrange
        var delayer = new GatedUpdateDelayer(FixedDelayer.NoneUnsafe);

        // act
        var delayTask = delayer.Delay(0);

        // assert
        delayer.IsOpen.Should().BeFalse();
        delayTask.IsCompleted.Should().BeFalse();

        delayer.Open();
        await delayTask.WaitAsync(TimeSpan.FromSeconds(5));
        delayer.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task DelayPassesThroughWhileOpen()
    {
        // arrange
        var delayer = new GatedUpdateDelayer(FixedDelayer.NoneUnsafe, isOpen: true);

        // act & assert
        await delayer.Delay(0).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CloseBlocksSubsequentDelays()
    {
        // arrange
        var delayer = new GatedUpdateDelayer(FixedDelayer.NoneUnsafe, isOpen: true);
        await delayer.Delay(0).WaitAsync(TimeSpan.FromSeconds(5));

        // act
        delayer.Close();
        var delayTask = delayer.Delay(0);

        // assert
        delayTask.IsCompleted.Should().BeFalse();
        delayer.Open();
        await delayTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DelayIsCancellable()
    {
        // arrange
        var delayer = new GatedUpdateDelayer(FixedDelayer.NoneUnsafe);
        using var cts = new CancellationTokenSource();

        // act
        var delayTask = delayer.Delay(0, cts.Token);
        cts.Cancel();

        // assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => delayTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
