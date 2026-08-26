namespace ActualChat.Users.UnitTests;

public sealed class RpcEndpointTagTest
{
    private static readonly IReadOnlySet<string> KnownHosts
        = new HashSet<string>(["voxt.ai", "actual.chat"], StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("voxt.ai", "origin")]
    [InlineData("actual.chat", "origin")]
    [InlineData("VOXT.AI", "origin")]
    [InlineData("kz1.edge.voxt.ai", "edge:kz1")]
    [InlineData("KZ1.edge.voxt.ai", "edge:kz1")]
    [InlineData("tr1.edge.dev.voxt.ai", "edge:tr1")]
    public void ShouldTagKnownEndpoints(string endpoint, string expected)
    {
        // act
        var tag = SystemProperties.EndpointTag(KnownHosts, endpoint);

        // assert
        tag.Should().Be(expected,
            because: "the split between direct and relayed connections is what the metric exists to show");
    }

    [Theory]
    [InlineData("")]
    [InlineData("evil.example.com")]
    [InlineData(".edge.voxt.ai")]
    [InlineData("a-b.edge.voxt.ai")]
    [InlineData("kz1!.edge.voxt.ai")]
    [InlineData("thisnameiswaytoolongtobereal.edge.voxt.ai")]
    public void ShouldCollapseAnythingUnrecognized(string endpoint)
    {
        // act
        var tag = SystemProperties.EndpointTag(KnownHosts, endpoint);

        // assert
        tag.Should().Be("other",
            because: "a client-supplied tag value must never open an unbounded set of time series");
    }

    [Fact]
    public void ShouldBoundTheTagSet()
    {
        // arrange
        var endpoints = Enumerable.Range(0, 500).Select(i => $"attacker{i}.example.com").ToArray();

        // act
        var tags = endpoints.Select(x => SystemProperties.EndpointTag(KnownHosts, x)).Distinct().ToArray();

        // assert
        tags.Should().Equal(["other"]);
    }
}
