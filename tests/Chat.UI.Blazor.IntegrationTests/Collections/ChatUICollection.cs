namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[CollectionDefinition(nameof(ChatUICollection))]
public class ChatUICollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture("chat-ui", messageSink);
