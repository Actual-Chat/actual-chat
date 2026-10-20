using ActualChat.Rpc;

namespace ActualChat.Core.UnitTests.Rpc;

public class RpcEndpointSelectorTest
{
    private const string Origin = "voxt.ai";
    private const string Edge1 = "node1.edge.voxt.ai";
    private const string Edge2 = "node2.edge.voxt.ai";
    private const string DevOrigin = "dev.voxt.ai";
    private const string DevEdge1 = "node1.edge.dev.voxt.ai";

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
    public void AttestationShouldBeKnownOnlyForTheHostItCameFrom()
    {
        // arrange
        var selector = new RpcEndpointSelector([Origin, Edge1]);

        // act
        selector.UseAttestation(Edge1, "line");

        // assert
        selector.GetAttestation(Edge1).Should().Be("line");
        selector.GetAttestation("NODE1.edge.voxt.ai").Should().Be("line");
        selector.GetAttestation(Origin).Should().BeNull();
        selector.GetAttestation(Edge2).Should().BeNull();
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
    public void ShouldSelectAnyCandidate()
    {
        // arrange
        var selector = new RpcEndpointSelector([Origin, Edge1, Edge2]);

        // act & assert
        selector.Use(Edge2).Should().BeTrue();
        selector.Current.Should().Be(Edge2);
        selector.Use("retired.edge.voxt.ai").Should().BeFalse(because: "only candidates may be dialed");
        selector.Current.Should().Be(Edge2);
    }

    [Fact]
    public void ShouldNotExpireTheVerdictOnMoveNextOrUse()
    {
        // arrange
        var selector = new RpcEndpointSelector([Origin, Edge1, Edge2]);
        var version = selector.Version;

        // act
        selector.MoveNext();
        selector.Use(Edge2);

        // assert
        selector.Version.Should().Be(version,
            because: "moving between endpoints is not a new network, so measurements still hold");
    }

    [Fact]
    public void ShouldExpireTheVerdictOnInvalidateWithoutMoving()
    {
        // arrange
        var selector = new RpcEndpointSelector([Origin, Edge1], Edge1);
        var version = selector.Version;

        // act
        selector.Invalidate();

        // assert
        selector.Current.Should().Be(Edge1,
            because: "a working endpoint must be kept until the new network is measured");
        selector.Version.Should().NotBe(version);
    }

    [Theory]
    [InlineData("wss://voxt.ai", "wss://node1.edge.voxt.ai")]
    [InlineData("https://voxt.ai/rpc/check?size=1", "https://node1.edge.voxt.ai/rpc/check?size=1")]
    [InlineData("https://cdn.voxt.ai/a.png", "https://node1.edge.voxt.ai/a.png")]
    [InlineData("not-a-url", "not-a-url")]
    public void WithHostShouldReplaceTheHostUnconditionally(string baseUrl, string expected)
        => RpcEndpointSelector.WithHost(baseUrl, Edge1).Should().Be(expected);

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
    [InlineData("wss://voxt.ai", "wss://node1.edge.voxt.ai")]
    [InlineData("https://voxt.ai", "https://node1.edge.voxt.ai")]
    [InlineData("https://voxt.ai/rpc/ws?x=1", "https://node1.edge.voxt.ai/rpc/ws?x=1")]
    [InlineData("https://voxt.ai:443/x", "https://node1.edge.voxt.ai:443/x")]
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
    public void ContentHostShouldStayOnTheOriginWhileRpcDoes()
    {
        // arrange
        var selector = new RpcEndpointSelector([Origin, Edge1]);

        // act & assert
        selector.GetContentHost("cdn.voxt.ai").Should().Be("cdn.voxt.ai");
    }

    [Theory]
    [InlineData(Origin, Edge1, "cdn.voxt.ai", "node1.edge.cdn.voxt.ai")]
    [InlineData(Origin, Edge1, "media.voxt.ai", "node1.edge.media.voxt.ai")]
    [InlineData(Origin, Edge1, "maps.voxt.ai", "node1.edge.maps.voxt.ai")]
    [InlineData(Origin, Edge2, "cdn.voxt.ai", "node2.edge.cdn.voxt.ai")]
    [InlineData(Origin, Edge1, "voxt.ai", "voxt.ai")]
    [InlineData(Origin, Edge1, "tiles.openfreemap.org", "tiles.openfreemap.org")]
    [InlineData(Origin, Edge1, "evilvoxt.ai", "evilvoxt.ai")]
    [InlineData(DevOrigin, DevEdge1, "cdn.dev.voxt.ai", "node1.edge.cdn.dev.voxt.ai")]
    [InlineData(DevOrigin, DevEdge1, "media.dev.voxt.ai", "node1.edge.media.dev.voxt.ai")]
    [InlineData(DevOrigin, DevEdge1, "maps.dev.voxt.ai", "node1.edge.maps.dev.voxt.ai")]
    [InlineData(DevOrigin, DevEdge1, "dev.voxt.ai", "dev.voxt.ai")]
    [InlineData(DevOrigin, DevEdge1, "cdn.voxt.ai", "cdn.voxt.ai")]
    [InlineData(DevOrigin, DevEdge1, "evildev.voxt.ai", "evildev.voxt.ai")]
    public void ContentHostShouldFollowTheEdgeNodeOnlyForOriginSubdomains(
        string origin, string endpoint, string host, string expected)
    {
        // arrange
        var selector = new RpcEndpointSelector([origin, endpoint], endpoint);

        // act & assert
        selector.GetContentHost(host).Should().Be(expected,
            because: "only the origin's own subdomains have an edge counterpart");
    }

    [Theory]
    [InlineData("https://cdn.voxt.ai/media/a.png", "https://node1.edge.cdn.voxt.ai/media/a.png")]
    [InlineData(
        "https://media.voxt.ai/64x64/https://cdn.voxt.ai/media/a.png",
        "https://node1.edge.media.voxt.ai/64x64/https://cdn.voxt.ai/media/a.png")]
    [InlineData("https://maps.voxt.ai/styles/liberty?v=1", "https://node1.edge.maps.voxt.ai/styles/liberty?v=1")]
    [InlineData("https://voxt.ai/rpc/ws", "https://voxt.ai/rpc/ws")]
    public void ApplyToContentShouldReplaceOnlyTheHost(string url, string expected)
    {
        // arrange
        RpcEndpointSelector.Instance = new RpcEndpointSelector([Origin, Edge1], Edge1);
        try {
            // act & assert
            RpcEndpointSelector.ApplyToContent(url).Should().Be(expected);
        }
        finally {
            RpcEndpointSelector.Instance = null;
        }
    }

    [Theory]
    [InlineData(Edge1, "https://cdn.voxt.ai/a.png", true, "https://node1.edge.cdn.voxt.ai/a.png")]
    [InlineData(Edge1, "https://voxt.ai/rpc/ws", false, "https://voxt.ai/rpc/ws")]
    [InlineData(Origin, "https://cdn.voxt.ai/a.png", false, "https://cdn.voxt.ai/a.png")]
    public void IsContentRemappedShouldReportWhetherTheHostChanged(
        string endpoint, string url, bool mustBeRemapped, string expectedUrl)
    {
        // arrange
        RpcEndpointSelector.Instance = new RpcEndpointSelector([Origin, Edge1], endpoint);
        try {
            // act
            var isRemapped = RpcEndpointSelector.IsContentRemapped(url, out var contentUrl);

            // assert
            isRemapped.Should().Be(mustBeRemapped);
            contentUrl.Should().Be(expectedUrl, because: "a URL that isn't remapped comes back as it is");
        }
        finally {
            RpcEndpointSelector.Instance = null;
        }
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
