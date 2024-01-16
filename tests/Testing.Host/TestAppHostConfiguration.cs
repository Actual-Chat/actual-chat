using Microsoft.Extensions.Configuration;

namespace ActualChat.Testing.Host;

public class TestAppHostConfiguration
{
    public static readonly TestAppHostConfiguration WithDefaultChat = new() {
        ConfigureServices = c => c.AddChatDbDataInitialization(o => o.AddDefaultChat = true)
    };

    public Action<IConfigurationBuilder>? ConfigureAppSettings { get; init; }
    public Action<IServiceCollection>? ConfigureServices { get; init; }
    public string? ServerUrls { get; init; }
}
