using ActualChat.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[CollectionDefinition(nameof(ChatCollection))]
public class ChatCollection : ICollectionFixture<ChatCollection.AppHostFixture>
{
    public class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture("chat", messageSink, TestAppHostOptions.WithDefaultChat with {
            // Web hook delivery tests POST to an in-test receiver on loopback, which the egress guard blocks by
            // default. CoreServerSettings binds from the "CoreSettings" section (see CoreServerModule.LoadSettings),
            // not the "CoreServerSettings" section AddInMemory<CoreServerSettings> would use.
            ConfigureHost = (_, cfg) => cfg.AddInMemoryCollection(
                ($"{nameof(CoreSettings)}:{nameof(CoreServerSettings.EgressHostAllowList)}:0", "localhost"),
                ($"{nameof(CoreSettings)}:{nameof(CoreServerSettings.EgressHostAllowList)}:1", "127.0.0.1")),
        });
}
