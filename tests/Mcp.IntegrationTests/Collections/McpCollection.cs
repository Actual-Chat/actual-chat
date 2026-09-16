using ActualChat.Media.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[CollectionDefinition(nameof(McpCollection))]
public class McpCollection : ICollectionFixture<McpCollection.AppHostFixture>
{
    public class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture("mcp", messageSink, TestAppHostOptions.Default with {
            // upload_from_url tests fetch from the test host itself, which the egress guard blocks by default
            ConfigureHost = (_, cfg) => cfg.AddInMemory<MediaSettings>((x => x.CrawlingHostAllowList, "localhost")),
        });
}
