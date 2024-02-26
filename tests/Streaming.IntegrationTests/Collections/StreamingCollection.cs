namespace ActualChat.Streaming.IntegrationTests;

[CollectionDefinition(nameof(StreamingCollection))]
public class StreamingCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture("streaming", messageSink);
