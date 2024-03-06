using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[CollectionDefinition(nameof(ChatActivityCollection))]
public class ChatActivityCollection : ICollectionFixture<ChatActivityCollection.AppHostFixture>
{
    public class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture("chat-activity", messageSink, TestAppHostOptions.WithDefaultChat);
}


