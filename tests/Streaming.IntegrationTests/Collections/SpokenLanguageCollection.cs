using ActualChat.Chat.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Streaming.IntegrationTests;

// Its own host because the entry language row is only readable with translation on, and the
// streaming collection's tests must not pay for translation they never ask about.
[CollectionDefinition(nameof(SpokenLanguageCollection))]
public class SpokenLanguageCollection : ICollectionFixture<SpokenLanguageCollection.AppHostFixture>
{
    public class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture("spokenlang", messageSink, TestAppHostOptions.Default with {
            ConfigureHost = (_, cfg) => {
                cfg.AddInMemory<ChatSettings>((x => x.IsTranslationEnabled, "true"));
            },
        });
}
