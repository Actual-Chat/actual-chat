namespace ActualChat.UI.Blazor.PlaywrightTests;

[CollectionDefinition(nameof(UIAutomationCollection))]
public class UIAutomationCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(messageSink)
{
    public override string DbInstanceName => "ui-automation";
}
