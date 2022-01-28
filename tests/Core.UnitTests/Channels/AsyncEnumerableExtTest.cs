namespace ActualChat.Core.UnitTests.Channels;

public class AsyncEnumerableExtTest
{
    [Fact]
    public async Task BasicMergeTest()
    {
        var left = Left();
        var right = Right();
        var result = left.Merge(right);
        var resultList = await result.ToListAsync();
        resultList.Should().BeEquivalentTo(new[] { 0, 1, 2, 10, 3, 4, 5, 20, 6, 30 }, options => options.WithStrictOrdering());

        async IAsyncEnumerable<int> Left()
        {
            yield return 0;

            await Task.Delay(TimeSpan.FromMilliseconds(100));

            yield return 10;

            await Task.Delay(TimeSpan.FromMilliseconds(100));

            yield return 20;

            await Task.Delay(TimeSpan.FromMilliseconds(300));

            yield return 30;
        }

        async IAsyncEnumerable<int> Right()
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            yield return 1;
            yield return 2;

            await Task.Delay(TimeSpan.FromMilliseconds(150));

            yield return 3;
            yield return 4;

            await Task.Delay(TimeSpan.FromMilliseconds(10));

            yield return 5;

            await Task.Delay(TimeSpan.FromMilliseconds(100));

            yield return 6;
        }
    }


    [Fact]
    public async Task MergeCancellationTest()
    {
        var cts = new CancellationTokenSource();
        var left = Left(cts.Token);
        var right = Right(cts.Token);
        var result = left.Merge(right);
        await foreach (var n in result) {
            if (n == 10)
                cts.Cancel();
        }

        async IAsyncEnumerable<int> Left([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return 0;

            await Task.Delay(TimeSpan.FromMilliseconds(100));

            yield return 10;

            await Task.Delay(TimeSpan.FromMilliseconds(100));

            yield return 20;

            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(TimeSpan.FromMilliseconds(300));

            yield return 30;
        }

        async IAsyncEnumerable<int> Right([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            yield return 1;
            yield return 2;

            await Task.Delay(TimeSpan.FromMilliseconds(150));

            yield return 3;
            yield return 4;

            await Task.Delay(TimeSpan.FromMilliseconds(10));

            yield return 5;

            await Task.Delay(TimeSpan.FromMilliseconds(100));

            cancellationToken.ThrowIfCancellationRequested();

            yield return 6;
        }
    }
}
