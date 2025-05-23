namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[CollectionDefinition(nameof(ChatUICollection))]
public class ChatUICollection : ICollectionFixture<ChatAppHostFixture>;

public class ChatAppHostFixture(IMessageSink messageSink) : Testing.Host.AppHostFixture("chat-ui", messageSink);
