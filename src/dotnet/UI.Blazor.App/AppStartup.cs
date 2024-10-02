using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.WebSockets;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Security;
using ActualChat.UI.Blazor.Module;
using ActualLab.Interception;
using ActualLab.Internal;
using ActualLab.RestEase;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;
using ActualLab.Rpc.WebSockets;

namespace ActualChat.UI.Blazor.App;

public static class AppStartup
{
    [RequiresUnreferencedCode(UnreferencedCode.Reflection)]
    public static void ConfigureServices(
        IServiceCollection services,
        HostKind hostKind,
        Func<IServiceProvider, HostModule[]>? platformModuleFactory,
        Tracer? rootTracer = null)
    {
        var tracer = (rootTracer ?? Tracer.Default)[typeof(AppStartup)];
#if !DEBUG
        Interceptor.Options.Defaults.IsValidationEnabled = false;
#else
        if (hostKind.IsMauiApp())
            Interceptor.Options.Defaults.IsValidationEnabled = false;
#endif

        // Fusion services
        var fusion = services.AddFusion();
        var restEase = services.AddRestEase();
        restEase.ConfigureHttpClient((c, name, o) => {
            var urlMapper = c.UrlMapper();
            var clientBaseUrl = urlMapper.ApiBaseUrl.ToUri();
            o.HttpClientActions.Add(client => {
                client.BaseAddress = clientBaseUrl;
                client.DefaultRequestVersion = OSInfo.IsAndroid
                    ? HttpVersion.Version20
                    : HttpVersion.Version30;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
                // c.LogFor(typeof(AppStartup)).LogInformation(
                //     "HTTP client '{Name}' configured @ {BaseAddress}", name, client.BaseAddress);
                if (!hostKind.IsMauiApp())
                    return;

                var gclbCookieHeader = AppLoadBalancerSettings.Instance.GclbCookieHeader;
                client.DefaultRequestHeaders.Add(gclbCookieHeader.Name, gclbCookieHeader.Value);
                if (c.GetService<TrueSessionResolver>() is { HasSession: true } trueSessionResolver) {
                    var session = trueSessionResolver.Session;
                    client.DefaultRequestHeaders.Add(Constants.Session.HeaderName, session.Id.Value);
                }
            });
            if (hostKind.IsMauiApp())
                o.HttpMessageHandlerBuilderActions.Add(b => {
                    if (b.PrimaryHandler is HttpClientHandler h)
                        h.UseCookies = false;
                });
        });

        fusion.Rpc.AddWebSocketClient(c => {
            var options = new RpcWebSocketClient.Options() {
                ConnectionUriResolver = (client, peer) => {
                    if (peer.Ref != RpcPeerRef.Default)
                        throw StandardError.Internal("Client-side RpcPeer.Ref != RpcPeerRef.Default.");

                    var settings = client.Settings;
                    var urlMapper = client.Services.UrlMapper();
                    var sb = ActualLab.Text.StringBuilderExt.Acquire();
                    sb.Append(urlMapper.WebsocketBaseUrl);
                    sb.Append(settings.RequestPath);
                    sb.Append('?');
                    sb.Append(settings.ClientIdParameterName);
                    sb.Append('=');
                    sb.Append(peer.ClientId.Value); // Always Url-encoded
                    sb.Append('&');
                    sb.Append(settings.SerializationFormatParameterName);
                    sb.Append('=');
                    sb.Append(peer.SerializationFormat.Key.Value);
                    return sb.ToStringAndRelease().ToUri();
                },
            };
            if (hostKind.IsMauiApp())
                // NOTE(AY): "new ClientWebSocket()" triggers this exception in WASM:
                // - PlatformNotSupportedException: Operation is not supported on this platform.
                // So the code below should never run in WASM.
                options = options with {
                    WebSocketOwnerFactory = (client, peer) => {
                        var ws = new ClientWebSocket();
                        var gclbCookieHeader = AppLoadBalancerSettings.Instance.GclbCookieHeader;
                        ws.Options.SetRequestHeader(gclbCookieHeader.Name, gclbCookieHeader.Value);
                        if (c.GetService<TrueSessionResolver>() is { HasSession: true } trueSessionResolver)
                            ws.Options.SetRequestHeader(Constants.Session.HeaderName, trueSessionResolver.Session.Id.Value);
                        if (Constants.Api.Compression.IsClientSideEnabled)
                            ws.Options.DangerousDeflateOptions = new WebSocketDeflateOptions();
                        return new WebSocketOwner(peer.Ref.Key, ws, client.Services);
#if false
                        // Non-native Android WebSocket stack requires SocketsHttpHandler to support TLS 1.2
                        var handler = new SocketsHttpHandler() {
                            SslOptions = new SslClientAuthenticationOptions() {
                                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            },
                        };
                        return new WebSocketOwner(peer.Ref.Key, ws, client.Services) { Handler = handler };
#endif
                    },
                };
            return options;
        });

        // Creating modules
        using var _ = tracer.Region($"{nameof(ModuleHostBuilder)}.{nameof(ModuleHostBuilder.Build)}");
        var moduleServices = services.BuildServiceProvider();
        var moduleHostBuilder = new ModuleHostBuilder()
            // From less dependent to more dependent!
            .AddModules(
                // Core modules
                new CoreModule(moduleServices),
                // API
                new ApiModule(moduleServices),
                new ApiContractsModule(moduleServices),
                // UI modules
                new BlazorUICoreModule(moduleServices),
                // This module should be the last one
                new BlazorUIAppModule(moduleServices)
            );
        if (platformModuleFactory != null)
            moduleHostBuilder = moduleHostBuilder.AddModules(platformModuleFactory.Invoke(moduleServices));
        moduleHostBuilder.Build(services);
    }
}
