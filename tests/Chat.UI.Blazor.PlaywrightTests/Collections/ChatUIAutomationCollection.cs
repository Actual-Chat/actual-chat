namespace ActualChat.UI.Blazor.App.PlaywrightTests;

[CollectionDefinition(nameof(ChatUIAutomationCollection))]
public class ChatUIAutomationCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("chat-ui-automation", messageSink);
