using ActualChat.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[CollectionDefinition(nameof(McpCollection))]
public class McpCollection : ICollectionFixture<McpCollection.AppHostFixture>
{
    public class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture("mcp", messageSink, TestAppHostOptions.Default with {
            // upload_from_url tests fetch from the test host itself, which the egress guard blocks by default.
            // CoreServerSettings binds from the "CoreSettings" section (see CoreServerModule.LoadSettings),
            // not the "CoreServerSettings" section AddInMemory<CoreServerSettings> would use.
            ConfigureHost = (_, cfg) => cfg.AddInMemoryCollection(
                ($"{nameof(CoreSettings)}:{nameof(CoreServerSettings.EgressHostAllowList)}:0", "localhost")),
        });
}
