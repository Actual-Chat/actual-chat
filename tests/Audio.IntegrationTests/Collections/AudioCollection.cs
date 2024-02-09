namespace ActualChat.Audio.IntegrationTests;

[CollectionDefinition(nameof(AudioCollection))]
public class AudioCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(messageSink)
{
    public override string DbInstanceName => "audio";
}
