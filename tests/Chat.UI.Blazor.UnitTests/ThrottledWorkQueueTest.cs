using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ThrottledWorkQueueTest(ITestOutputHelper @out) : TestBase(@out)
{
    private string ConsumerId { get; } = UniqueNames.Name(nameof(ThrottledWorkQueueTest));

    [Theory]
    [InlineData(1, 2, true)]
    [InlineData(1, 2, false)]
    [InlineData(3, 10, true)]
    [InlineData(3, 10, false)]
    [InlineData(10, 20, true)]
    [InlineData(10, 20, false)]
    public async Task ShouldDequeue(int parallelDegree, int taskCount, bool cancelRunning)
    {
        // arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10).Debuggable());
        var cancellationToken = cts.Token;
        var ids = Enumerable.Range(1, taskCount).Select(i => i.ToString()).ToList();
        var fetcher = new Fetcher().Register(ids);

        await using var sut = new ThrottledWorkQueue<string, string>(parallelDegree, fetcher.Fetch, Out.ToLogger<ThrottledWorkQueue<string, string>>());
        sut.Start();

        // act
        foreach (var id in ids)
            await sut.Enqueue(id, ConsumerId, cancellationToken);

        // assert
        await TestExt.When(() => {
            sut.ListAll().Should().HaveCount(taskCount);
            sut.ListRunning().Select(x => x.Key).Should().BeEquivalentTo(ids[..parallelDegree]);
            sut.ListQueued().Select(x => x.Key).Should().BeEquivalentTo(ids[parallelDegree..]);
        }, TimeSpan.FromSeconds(5).Debuggable());

        // act
        sut.Dequeue(ConsumerId, cancelRunning, ids);

        // assert
        await TestExt.When(() => {
            sut.ListQueued().Should().BeEmpty();
            if (cancelRunning) {
                sut.ListRunning().Should().BeEmpty();
                sut.ListAll().Should().BeEmpty();
            }
            else {
                sut.ListRunning().Select(x => x.Key).Should().BeEquivalentTo(ids[..parallelDegree]);
                sut.ListAll().Select(x => x.Key).Should().BeEquivalentTo(ids[..parallelDegree]);
            }
        }, TimeSpan.FromSeconds(5).Debuggable());

        if (!cancelRunning) {
            foreach (var id in ids)
                fetcher.Cancel(id);

            await TestExt.When(() => {
                sut.ListQueued().Should().BeEmpty();
                sut.ListRunning().Should().BeEmpty();
                sut.ListAll().Should().BeEmpty();
            }, TimeSpan.FromSeconds(5).Debuggable());
        }
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 10)]
    [InlineData(10, 20)]
    public async Task ShouldRunEnqueuedToCompletion(int parallelDegree, int taskCount)
    {
        // arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10).Debuggable());
        var cancellationToken = cts.Token;
        var ids = Enumerable.Range(1, taskCount).Select(i => i.ToString()).ToList();
        var fetcher = new Fetcher().Register(ids);

        await using var sut = new ThrottledWorkQueue<string, string>(parallelDegree, fetcher.Fetch, Out.ToLogger<ThrottledWorkQueue<string, string>>());
        sut.Start();

        // act
        foreach (var id in ids)
            await sut.Enqueue(id, ConsumerId, cancellationToken);

        // assert
        await TestExt.When(() => {
            sut.ListAll().Should().HaveCount(taskCount);
            sut.ListRunning().Select(x => x.Key).Should().BeEquivalentTo(ids[..parallelDegree]);
            sut.ListQueued().Select(x => x.Key).Should().BeEquivalentTo(ids[parallelDegree..]);
        }, TimeSpan.FromSeconds(5).Debuggable());

        // act
        var workItems = ids.Select(sut.Get).ToList();
        fetcher.SetDefaultResults(ids);

        // assert
        await TestExt.When(() => {
            sut.ListQueued().Should().BeEmpty();
            sut.ListRunning().Should().BeEmpty();
            sut.ListAll().Should().BeEmpty();
        }, TimeSpan.FromSeconds(5).Debuggable());
        workItems.Should().AllSatisfy(x => x?.IsCompletedSuccessfully.Should().BeTrue());
    }

    private class Fetcher
    {
        private readonly Dictionary<string, TaskCompletionSource<string>> _taskSources = new(StringComparer.Ordinal);

        public void Cancel(string id)
            => _taskSources[id].TrySetCanceled();

        public void SetResult(string id, string? data = null)
            => _taskSources[id].TrySetResult(data ?? $"Data for #{id}");

        public void SetDefaultResults(IEnumerable<string> ids)
        {
            foreach (var id in ids)
                SetResult(id);
        }

        public Fetcher Register(params IEnumerable<string> ids)
        {
            foreach (var id in ids)
                _taskSources.GetOrAdd(id);
            return this;
        }

        public Task<string> Fetch(string id, CancellationToken cancellationToken)
            => _taskSources[id].Task.WaitAsync(cancellationToken);
    }
}
