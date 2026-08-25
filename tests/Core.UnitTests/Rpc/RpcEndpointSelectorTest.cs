using ActualChat.Rpc;

namespace ActualChat.Core.UnitTests.Rpc;

public class RpcEndpointSelectorTest
{
    private const string Origin = "voxt.ai";
    private const string Edge1 = "kz1.edge.voxt.ai";
    private const string Edge2 = "tr1.edge.voxt.ai";

    [Fact]
    public void ShouldStartOnTheOrigin()
    {
        // arrange
        var selector = new RpcEndpointSelector([Origin, Edge1]);

        // assert
        selector.Current.Should().Be(Origin);
        selector.IsOnOrigin.Should().BeTrue();
        selector.Get(Origin).Should().Be(Origin);
    }

    [Fact]
    public void ShouldRestoreAKnownStoredEndpoint()
    {
        // act
        var selector = new RpcEndpointSelector([Origin, Edge1], Edge1);

        // assert
        selector.Current.Should().Be(Edge1);
        selector.IsOnOrigin.Should().BeFalse();
    }

    [Fact]
    public void ShouldIgnoreAStoredEndpointThatIsNoLongerACandidate()
    {
        // act
        var selector = new RpcEndpointSelector([Origin, Edge1], "retired.edge.voxt.ai");

        // assert
        selector.Current.Should().Be(Origin,
            because: "a stored endpoint dropped from the candidate list must not be dialed");
    }

    [Fact]
    public void ShouldWalkCandidatesInOrderThenReportExhaustion()
    {
        // arrange
        var selector = new RpcEndpointSelector([Origin, Edge1, Edge2]);

        // act & assert
        selector.MoveNext().Should().BeTrue();
        selector.Current.Should().Be(Edge1);
        selector.MoveNext().Should().BeTrue();
        selector.Current.Should().Be(Edge2);
        selector.MoveNext().Should().BeFalse(because: "there is nothing after the last candidate");
    }

    [Fact]
    public void ShouldReturnToTheOriginOnUseDirect()
    {
        // arrange
        var selector = new RpcEndpointSelector([Origin, Edge1]);
        selector.MoveNext();

        // act
        selector.UseDirect();

        // assert
        selector.Current.Should().Be(Origin);
        selector.IsOnOrigin.Should().BeTrue();
    }

    [Fact]
    public void ShouldBumpVersionEvenWhenTheEndpointDoesNotChange()
    {
        // arrange
        var selector = new RpcEndpointSelector([Origin, Edge1]);
        var version = selector.Version;

        // act
        selector.UseDirect();

        // assert
        selector.Current.Should().Be(Origin);
        selector.Version.Should().NotBe(version,
            because: "a network change must expire an earlier verdict about this endpoint");
    }

    [Fact]
    public void ShouldOnlyRemapTheOriginHost()
    {
        // arrange
        var selector = new RpcEndpointSelector([Origin, Edge1]);
        selector.MoveNext();

        // assert
        selector.Get(Origin).Should().Be(Edge1);
        selector.Get("cdn.voxt.ai").Should().Be("cdn.voxt.ai",
            because: "content hosts must stay on their own origins");
    }

    [Theory]
    [InlineData("wss://voxt.ai", "wss://kz1.edge.voxt.ai")]
    [InlineData("https://voxt.ai", "https://kz1.edge.voxt.ai")]
    [InlineData("https://voxt.ai/rpc/ws?x=1", "https://kz1.edge.voxt.ai/rpc/ws?x=1")]
    [InlineData("https://voxt.ai:443/x", "https://kz1.edge.voxt.ai:443/x")]
    [InlineData("https://cdn.voxt.ai/a.png", "https://cdn.voxt.ai/a.png")]
    public void ApplyToShouldReplaceOnlyTheHost(string baseUrl, string expected)
    {
        // arrange
        var selector = new RpcEndpointSelector([Origin, Edge1]);
        selector.MoveNext();
        RpcEndpointSelector.Instance = selector;
        try {
            // act & assert
            RpcEndpointSelector.ApplyTo(baseUrl).Should().Be(expected);
        }
        finally {
            RpcEndpointSelector.Instance = null;
        }
    }

    [Fact]
    public void ApplyToShouldBeANoOpWithoutASelector()
    {
        // arrange
        RpcEndpointSelector.Instance = null;

        // act & assert
        RpcEndpointSelector.ApplyTo("wss://voxt.ai").Should().Be("wss://voxt.ai");
    }

    [Fact]
    public void ShouldReportChangesForPersistence()
    {
        // arrange
        var changes = new List<string>();
        var selector = new TestSelector([Origin, Edge1], changes);

        // act
        selector.MoveNext();
        selector.UseDirect();
        selector.UseDirect();

        // assert
        changes.Should().Equal([Edge1, Origin],
            because: "a reset that selects the same endpoint again is not a change");
    }

    // Nested types

    private sealed class TestSelector(string[] candidates, List<string> changes)
        : RpcEndpointSelector(candidates)
    {
        protected override void OnChanged(string endpoint)
            => changes.Add(endpoint);
    }
}
