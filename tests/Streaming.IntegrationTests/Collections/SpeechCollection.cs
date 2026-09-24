using ActualChat.Transcription.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Streaming.IntegrationTests;

// Its own host so the synthesizer is the fake one: with a Soniox key in the environment the
// streaming host speaks over the network, which makes "was it asked to speak, and in what
// language" unanswerable - and that question is the whole point of these tests.
[CollectionDefinition(nameof(SpeechCollection))]
public class SpeechCollection : ICollectionFixture<SpeechCollection.AppHostFixture>
{
    public class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture("speech", messageSink, TestAppHostOptions.Default with {
            ConfigureHost = (_, cfg) => {
                cfg.AddInMemory<TranscriptionSettings>((x => x.UseFakeTranscriber, "true"));
            },
        });
}
