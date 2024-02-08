namespace ActualChat.Testing.Host.Collections;

[CollectionDefinition(nameof(AppHostTestCollection))]
public class AppHostTestCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : Host.AppHostFixture(messageSink)
{
    protected override string DbInstanceName => "test";
}
