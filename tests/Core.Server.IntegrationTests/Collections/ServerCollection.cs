namespace ActualChat.Core.Server.IntegrationTests;

[CollectionDefinition(nameof(ServerCollection))]
public class ServerCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(messageSink)
{
    public override string DbInstanceName => "server";
}
