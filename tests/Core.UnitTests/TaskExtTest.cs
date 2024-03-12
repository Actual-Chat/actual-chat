namespace ActualChat.Core.UnitTests;

public class TaskExtTest(ITestOutputHelper @out)
{
    public ITestOutputHelper Out { get; } = @out;

    [Fact]
    public async Task CollectWithConcurrencyKeepsTasksLimited()
    {
        const int limit = 10;
        var clocks = MomentClockSet.Default.CpuClock;
        var activeTaskCount = 0;
        var parallelismInTime = await Enumerable.Repeat(0, 100)
            .Select(_ => IncrementWhileRunning())
            .Collect(limit);
        parallelismInTime.Should().Contain(10);

        Out.WriteLine(parallelismInTime.ToDelimitedString(", "));

        return;

        async Task<int> IncrementWhileRunning()
        {
            try {
                var result = Interlocked.Increment(ref activeTaskCount);
                result.Should().BeLessThanOrEqualTo(limit + 1);
                await clocks.Delay(Random.Shared.Next(100, 500)).ConfigureAwait(false);
                return result;
            }
            finally {
                Interlocked.Decrement(ref activeTaskCount);
            }
        }
    }
}
