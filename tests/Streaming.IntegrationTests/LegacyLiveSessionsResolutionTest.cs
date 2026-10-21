using ActualChat.Testing.Host;
using ActualLab.Rpc;

namespace ActualChat.Streaming.IntegrationTests;

/// <summary>
/// Resolves the wire ids old clients send through the server's own RPC registry, the way a live
/// connection does: by the hash of <c>Service.Method:ParameterCount</c> and the peer's API version.
/// </summary>
[Collection(nameof(StreamingCollection))]
public sealed class LegacyLiveSessionsResolutionTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public void A221ClientsGetMyCallShouldReachLegacyGetMyCall()
    {
        // arrange
        var registry = AppHost.Services.RpcHub().ServiceRegistry;
        var serviceName = registry[typeof(ILiveSessions)].Name;
        var oldPeer = registry.GetServerMethodResolver(new VersionSet("Api=2.21.1"));
        var newPeer = registry.GetServerMethodResolver(new VersionSet($"Api={ApiConstants.VersionString}"));
        var oldName = RpcMethodDef.ComposeFullName(serviceName, "GetMyCall:2");
        var newName = RpcMethodDef.ComposeFullName(serviceName, "GetMyCall:3");
        var oldHash = new RpcMethodRef(oldName).HashCode;
        var newHash = new RpcMethodRef(newName).HashCode;
        Out.WriteLine($"{oldHash:x8} {oldName}, {newHash:x8} {newName}");

        // act
        var oldPeerOldId = oldPeer[oldHash];
        var oldPeerNewId = oldPeer[newHash];
        var newPeerNewId = newPeer[newHash];

        // assert
        serviceName.Should().Be(nameof(ILiveSessions), "2.21 clients name the service after the interface");
        oldPeerOldId.Should().NotBeNull("a 2.21 client polls GetMyCall:2");
        oldPeerOldId!.MethodInfo.Name.Should().Be(nameof(ILiveSessions.LegacyGetMyCall));
        oldPeerNewId.Should().NotBeNull();
        oldPeerNewId!.MethodInfo.Name.Should().Be(nameof(ILiveSessions.GetMyCall));
        newPeerNewId.Should().NotBeNull();
        newPeerNewId!.MethodInfo.Name.Should().Be(nameof(ILiveSessions.GetMyCall));
    }
}
