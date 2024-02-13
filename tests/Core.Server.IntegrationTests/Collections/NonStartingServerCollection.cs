using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests;

[CollectionDefinition(nameof(NonStartingServerCollection))]
public class NonStartingServerCollection : ICollectionFixture<NonStartingAppHostFixture>;

public class NonStartingAppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("ns-server", messageSink)
{
    protected override TestAppHostOptions CreateAppHostOptions()
        => base.CreateAppHostOptions() with { MustStart = false };
}
