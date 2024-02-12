namespace ActualChat.Chat.IntegrationTests;

[CollectionDefinition(nameof(ChatCollection))]
public class ChatCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(messageSink)
{
    public override string DbInstanceName => "chat";
}
