using Bunit;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class VirtualListRecoveryTest
{
    [Fact]
    public async Task UnresolvedWindowShouldKeepRenderedItemsAndRetryTheQuery()
    {
        // arrange
        using var context = TestBunitContext.New();
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var initial = new VirtualListData<TestItem>([new("10")]);
        var recovered = new VirtualListData<TestItem>([new("11")]);
        var source = new TestDataSource(initial, new([]), recovered);
        var cut = context.Render<TestList>(p => p
            .Add(x => x.DataSource, source)
            .Add(x => x.SkipPreRenderGetDataCall, true));
        cut.WaitForAssertion(() => cut.Markup.Should().Be("10"));
        var query = new VirtualListDataQuery(new("10", "10"), default, new(0, 20));

        // act
        await cut.InvokeAsync(() => cut.Instance.RequestAndWait(query));

        // assert
        cut.Instance.CurrentData.Should().BeSameAs(initial);
        cut.WaitForAssertion(() => cut.Instance.CurrentData.Should().BeSameAs(recovered), TimeSpan.FromSeconds(3));
        source.Queries.ToArray().Should().Equal(VirtualListDataQuery.None, query, query);
        cut.Markup.Should().Be("11");
    }

    [Fact]
    public void InitiallyUnresolvedWindowShouldRecoverWithoutBrowserRequests()
    {
        // arrange
        using var context = TestBunitContext.New();
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var recovered = new VirtualListData<TestItem>([new("10")]);
        var source = new TestDataSource(new([]), recovered);

        // act
        var cut = context.Render<TestList>(p => p
            .Add(x => x.DataSource, source)
            .Add(x => x.SkipPreRenderGetDataCall, true));

        // assert
        cut.WaitForAssertion(() => cut.Instance.CurrentData.Should().BeSameAs(recovered), TimeSpan.FromSeconds(3));
        source.Queries.ToArray().Should().Equal(VirtualListDataQuery.None, VirtualListDataQuery.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefinitivelyEmptyWindowShouldRenderWithoutRetrying(bool isNone)
    {
        // arrange
        using var context = TestBunitContext.New();
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var initial = new VirtualListData<TestItem>([new("10")]);
        var empty = isNone
            ? VirtualListData<TestItem>.None
            : new([]) { HasVeryFirstItem = true, HasVeryLastItem = true };
        var source = new TestDataSource(initial, empty);
        var cut = context.Render<TestList>(p => p
            .Add(x => x.DataSource, source)
            .Add(x => x.SkipPreRenderGetDataCall, true));
        cut.WaitForAssertion(() => cut.Markup.Should().Be("10"));

        // act
        await cut.InvokeAsync(() => cut.Instance.RequestAndWait(new(default, default, default)));

        // assert
        cut.WaitForAssertion(() => cut.Markup.Should().Be("empty"));
        await Task.Delay(TimeSpan.FromSeconds(1.3));
        cut.Instance.CurrentData.Should().BeSameAs(empty);
        source.Queries.Should().HaveCount(2);
    }

    [Fact]
    public async Task NewQueryShouldSupersedeAnUnresolvedQuery()
    {
        // arrange
        using var context = TestBunitContext.New();
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var initial = new VirtualListData<TestItem>([new("10")]);
        var recovered = new VirtualListData<TestItem>([new("30")]);
        var source = new TestDataSource(initial, new([]), recovered);
        var cut = context.Render<TestList>(p => p
            .Add(x => x.DataSource, source)
            .Add(x => x.SkipPreRenderGetDataCall, true));
        cut.WaitForAssertion(() => cut.Markup.Should().Be("10"));
        var oldQuery = new VirtualListDataQuery(new("10", "10"), default, new(0, 20));
        var newQuery = new VirtualListDataQuery(new("30", "30"), default, new(0, 20));
        await cut.InvokeAsync(() => cut.Instance.RequestAndWait(oldQuery));

        // act
        await cut.InvokeAsync(() => cut.Instance.RequestAndWait(newQuery));

        // assert
        cut.WaitForAssertion(() => cut.Instance.CurrentData.Should().BeSameAs(recovered));
        await Task.Delay(TimeSpan.FromSeconds(1.3));
        source.Queries.ToArray().Should().Equal(VirtualListDataQuery.None, oldQuery, newQuery);
    }

    // Nested types

    private sealed class TestItem(string key) : IVirtualListItem
    {
        public string Key { get; } = key;
        public bool IsGroup => false;
        public bool ShouldSkipKey => false;
    }

    private sealed class TestDataSource(params VirtualListData<TestItem>[] responses) : IVirtualListDataSource<TestItem>
    {
        private int _callCount;
        public ConcurrentQueue<VirtualListDataQuery> Queries { get; } = new();

        public Task<VirtualListData<TestItem>> GetData(
            VirtualListDataQuery query,
            VirtualListData<TestItem> renderedData,
            CancellationToken cancellationToken)
        {
            Queries.Enqueue(query);
            var index = Math.Min(Interlocked.Increment(ref _callCount) - 1, responses.Length - 1);
            var response = responses[index];
            return Task.FromResult(response.IsSimilarTo(renderedData) ? renderedData : response);
        }
    }

    private sealed class TestList : VirtualList<TestItem>
    {
        public VirtualListData<TestItem> CurrentData => Data;

        public async Task RequestAndWait(VirtualListDataQuery query)
        {
            var whenUpdated = State.Snapshot.WhenUpdated();
            await RequestData(query);
            await whenUpdated.WaitAsync(TimeSpan.FromSeconds(3));
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            RenderedData = Data;
            RenderIndex++;
            builder.AddContent(0, Data.FirstItem?.Key ?? "empty");
        }

        protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
        protected override bool ShouldRender() => true;
        protected override ValueTask<IJSObjectReference> CreateJSRef() => throw new NotSupportedException();
    }
}
