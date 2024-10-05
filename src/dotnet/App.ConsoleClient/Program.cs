using ActualChat.Chat;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualLab.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Hosting;
using static System.Console;

// NOTE(AY): This could be removed once we update prod to the latest version
RpcSerializationFormatResolver.Default = RpcSerializationFormatResolver.Default with {
    DefaultClientFormatKey = "mempack1",
};

var cancellationTokenSource = new CancellationTokenSource();
var cancellationToken = cancellationTokenSource.Token;
CancelKeyPress += (_, args) => {
    args.Cancel = true;
    cancellationTokenSource.Cancel();
};

var services = CreateServiceProvider();
var chats = services.GetRequiredService<IChats>();
var authors = services.GetRequiredService<IAuthors>();

var session = new Session(GetArgument("s", "session", "your Session ID"));
var chatId = new ChatId(GetArgument("c", "chatId", "Chat ID to watch"));

var chat = await chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
if (chat == null)
    throw new InvalidOperationException($"No chat with ID = '{chatId}'.");

WriteLine($"Observing '{chat.Title}'...");
var chatNews = await chats.GetNews(session, chatId, cancellationToken).ConfigureAwait(false);
var reader = new ChatEntryReader(chats, session, chatId, ChatEntryKind.Text);
var entries = reader.Observe(chatNews.LastTextEntry?.Id.LocalId ?? 0, cancellationToken);
await foreach (var entry in entries.ConfigureAwait(false)) {
    var e = entry;
    if (entry.IsStreaming) {
        var c = await Computed
            .New(async ct => await reader.Get(entry.Id.LocalId, ct).ConfigureAwait(false))
            .When((x, _) => x is not { IsStreaming: true })
            .ConfigureAwait(false);
        e = c.Value;
        if (e == null) // Means it's deleted already
            continue;
    }
    var author = await authors.Get(session, chatId, e.AuthorId, cancellationToken).ConfigureAwait(false);
    WriteLine($"{author?.Avatar.Name ?? "(anonymous author)"}: {entry.Content}");
}

string GetArgument(string shortName, string longName, string? prompt = null)
{
    var prefix = $"-{shortName}:";
    var value = args.Where(x => x.OrdinalStartsWith(prefix)).Select(x => x[prefix.Length..]).LastOrDefault();
    if (!value.IsNullOrEmpty())
        return value;
    prefix = $"-{longName}:";
    value = args.Where(x => x.OrdinalStartsWith(prefix)).Select(x => x[prefix.Length..]).LastOrDefault();
    if (!value.IsNullOrEmpty())
        return value;

    value = Environment.GetEnvironmentVariable(longName);
    if (!value.IsNullOrEmpty())
        return value;

    Write($"Enter {prompt ?? longName}: ");
    return ReadLine()!.Trim();
}

IServiceProvider CreateServiceProvider()
{
    var cfg = new ConfigurationManager();
    var env = Environments.Production;
    cfg.Sources.Add(new MemoryConfigurationSource() {
        InitialData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) {
            { "DOTNET_ENVIRONMENT", env },
        },
    });

    // ReSharper disable once VariableHidesOuterVariable
    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(cfg);
    services.AddLogging(logging => {
        logging.ClearProviders();
        logging.SetMinimumLevel(LogLevel.Warning);
        logging.AddConsole();
    });
    services.AddTracers(Tracer.Default, useScopedTracers: true);
    services.AddSingleton(_ => {
        var baseUrl = "https://actual.chat/";
        return new HostInfo {
            HostKind = HostKind.MauiApp,
            AppKind = AppKind.Windows,
            Configuration = cfg,
            Environment = env,
            BaseUrl = baseUrl,
        };
    });

    var moduleServices = services.BuildServiceProvider();
    var moduleHostBuilder = new ModuleHostBuilder();
    var moduleHost = moduleHostBuilder.AddModules(
        // From less dependent to more dependent!
        new CoreModule(moduleServices),
        new ApiModule(moduleServices),
        new ApiContractsModule(moduleServices)
    );
    moduleHost.Build(services);
    return services.BuildServiceProvider();
}
