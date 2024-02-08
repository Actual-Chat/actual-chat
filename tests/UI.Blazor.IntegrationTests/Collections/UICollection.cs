namespace ActualChat.UI.Blazor.IntegrationTests;

[CollectionDefinition(nameof(UICollection))]
public class UICollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(messageSink)
{
    protected override string DbInstanceName => "ui";
}
