using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[CollectionDefinition(nameof(McpCollection))]
public class McpCollection : ICollectionFixture<McpCollection.AppHostFixture>
{
    public class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture("mcp", messageSink, TestAppHostOptions.Default);
}
