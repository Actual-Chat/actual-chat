using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[CollectionDefinition(nameof(ChatUICollection))]
public sealed class ChatUICollection : ICollectionFixture<ChatAppHostFixture>;

public sealed class ChatAppHostFixture(IMessageSink messageSink)
    : Testing.Host.AppHostFixture("chat-ui", messageSink, TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => services.AddScoped<MicrophonePermissionHandler>(
            c => new TestMicrophonePermissionHandler(c.UIHub())),
    });
