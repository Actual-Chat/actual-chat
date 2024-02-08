namespace ActualChat.Transcription.IntegrationTests;

[CollectionDefinition(nameof(TranscriptionCollection))]
public class TranscriptionCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(messageSink)
{
    protected override string DbInstanceName => "transcription";
}
