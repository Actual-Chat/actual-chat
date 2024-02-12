namespace ActualChat.Chat.UI.Blazor.PlaywrightTests;

[CollectionDefinition(nameof(ChatUIAutomationCollection))]
public class ChatUIAutomationCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(messageSink)
{
    public override string DbInstanceName => "chat-ui-automation";
}
