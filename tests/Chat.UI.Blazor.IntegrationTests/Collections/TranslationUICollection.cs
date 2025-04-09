using ActualChat.Chat.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[CollectionDefinition(nameof(TranslationUICollection))]
public class TranslationUICollection : ICollectionFixture<TranslationAppHostFixture>;

public class TranslationAppHostFixture(IMessageSink messageSink) : AppHostFixture("translation-ui",
    messageSink,
    TestAppHostOptions.Default with {
        ConfigureHost = (_, cfg) => {
            cfg.AddInMemory<ChatSettings>((x => x.IsTranslationEnabled, "true"));
        },
    });
