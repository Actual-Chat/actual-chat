using ActualChat.Testing.Host;
using ActualLab.Rpc;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ApiCommandVersionSeamTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public void ServerInboundMsgpackFormatsUseTheCompatSerializer()
    {
        // With a server AppHost up, CoreModuleInitializer has wired ApiCommandRpcArgumentSerializer into every
        // inbound msgpack arg serializer, so a legacy client's commands still deserialize on the negotiated format.
        var msgpackFormats = RpcSerializationFormat.All
            .Where(f => f.Key.StartsWith("msgpack"))
            .ToList();

        msgpackFormats.Should().NotBeEmpty();
        foreach (var format in msgpackFormats)
            format.ArgumentSerializer.Should().BeOfType<ApiCommandRpcArgumentSerializer>();
    }
}
