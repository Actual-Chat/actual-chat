namespace ActualChat.Core.Server.IntegrationTests;

[CollectionDefinition(nameof(NonStartingServerCollection))]
public class NonStartingServerCollection : ICollectionFixture<NonStartingAppHostFixture>;

public class NonStartingAppHostFixture : ActualChat.Testing.Host.AppHostFixture
{
    public NonStartingAppHostFixture(IMessageSink messageSink) : base("ns-server", messageSink)
        => AppHostOptions = AppHostOptions with { MustStart = false };
}
