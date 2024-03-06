using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[CollectionDefinition(nameof(PlaceCollection))]
public class PlaceCollection : ICollectionFixture<PlaceCollection.AppHostFixture>
{
    public class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture("place", messageSink, TestAppHostOptions.WithDefaultChat);
}


