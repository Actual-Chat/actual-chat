using ActualChat.Rpc;
using ActualLab.Rpc;

namespace ActualChat.Api.Contracts.UnitTests.Rpc;

public class RelayRefTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData("kz1.edge.voxt.ai", 1)]
    [InlineData("voxt.ai", 4096)]
    public void ShouldRoundTripThroughRpcRef(string host, int attempt)
    {
        // arrange
        var relay = new RelayRef(host, attempt);

        // act
        var rpcRef = relay.ToRpcRef();

        // assert
        RelayRef.TryParse(rpcRef, out var parsed).Should().BeTrue();
        parsed.Should().Be(relay);
        RelayRef.IsRelayRef(rpcRef).Should().BeTrue();
    }

    [Fact]
    public void AttemptsShouldGiveDifferentRefs()
    {
        // act & assert
        new RelayRef("voxt.ai", 1).ToRpcRef().Should().NotBe(new RelayRef("voxt.ai", 2).ToRpcRef());
    }

    [Fact]
    public void DefaultRefShouldNotBeARelayRef()
    {
        // act & assert
        RelayRef.IsRelayRef(RpcRef.Default).Should().BeFalse();
    }

    [Theory]
    [InlineData("relay|")]
    [InlineData("relay||3")]
    [InlineData("relay|voxt.ai|x")]
    [InlineData("probe|voxt.ai|3")]
    public void MalformedRefShouldNotParse(string hostInfo)
    {
        // act & assert
        RelayRef.IsRelayRef(RpcRef.NewClient(hostInfo)).Should().BeFalse();
    }
}
