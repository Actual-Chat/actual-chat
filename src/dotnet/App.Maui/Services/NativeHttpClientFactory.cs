using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace ActualChat.App.Maui.Services;

public partial class NativeHttpClientFactory : IHttpClientFactory
{
    private readonly ConcurrentDictionary<string, HttpClient> _clients = new ();

    private IServiceProvider Services { get; }
    private IOptionsSnapshot<HttpClientFactoryOptions> Options { get; }
    private List<IHttpMessageHandlerBuilderFilter> Filters { get; }

    public NativeHttpClientFactory(
        IServiceProvider services,
        IOptionsSnapshot<HttpClientFactoryOptions> options,
        IEnumerable<IHttpMessageHandlerBuilderFilter> filters)
    {
        Services = services;
        Options = options;
        Filters = filters.ToList();
    }

    public HttpClient CreateClient(string name)
        => _clients.GetOrAdd(name, _ =>
            CreatePlatformMessageHandler() is { } handler
                ? ConfigureClient(new HttpClient(ConfigureMessageHandler(handler, name), false), name)
                : new HttpClient());

    private partial HttpMessageHandler? CreatePlatformMessageHandler();

    private HttpClient ConfigureClient(HttpClient client, string name)
    {
        var options = Options.Get(name);
        foreach (var httpClientAction in options.HttpClientActions)
            httpClientAction(client);

        return client;
    }

    private HttpMessageHandler ConfigureMessageHandler(HttpMessageHandler handler, string name)
    {
        var options = Options.Get(name);
        var builder = Services.GetRequiredService<HttpMessageHandlerBuilder>();
        builder.PrimaryHandler = handler;
        builder.Name = name;

        // This is similar to the initialization pattern in:
        // https://github.com/aspnet/Hosting/blob/e892ed8bbdcd25a0dafc1850033398dc57f65fe1/src/Microsoft.AspNetCore.Hosting/Internal/WebHost.cs#L188
        Action<HttpMessageHandlerBuilder> configure = Configure;
        for (int i = Filters.Count - 1; i >= 0; i--)
            configure = Filters[i].Configure(configure);

        configure(builder);

        void Configure(HttpMessageHandlerBuilder b)
        {
            for (int i = 0; i < options?.HttpMessageHandlerBuilderActions.Count; i++)
                options.HttpMessageHandlerBuilderActions[i](b);
        }

        return builder.Build();
    }
}
