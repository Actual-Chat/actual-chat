using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[CollectionDefinition(nameof(ChatCollection))]
public class ChatCollection : ICollectionFixture<ChatCollection.AppHostFixture>
{
    public class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture("chat", messageSink, TestAppHostOptions.WithDefaultChat);
}


