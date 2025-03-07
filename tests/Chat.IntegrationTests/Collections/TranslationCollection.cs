using ActualChat.Chat.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[CollectionDefinition(nameof(TranslationCollection))]
public class TranslationCollection : ICollectionFixture<TranslationCollection.AppHostFixture>
{
    public class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture("translate", messageSink, TestAppHostOptions.Default with {
            ConfigureHost = (_, cfg) => {
                cfg.AddInMemory<ChatSettings>((x => x.IsTranslationEnabled, "true"));
            },
        });
}
