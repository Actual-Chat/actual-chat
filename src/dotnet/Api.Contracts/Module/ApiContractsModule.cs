using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using ActualChat.Contacts;
using ActualChat.Invite;
using ActualChat.Kvas;
using ActualChat.Notifications;
using ActualChat.Search;
using ActualChat.Security;
using ActualChat.Streaming;
using ActualChat.Rpc;
using ActualLab.RestEase;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;
using ActualLab.Rpc.WebSockets;

namespace ActualChat.Module;

public sealed class ApiContractsModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IAppModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        var fusion = services.AddFusion();
        var rpc = fusion.Rpc;

        // Fusion & RestEase client
        ConfigureFusionClients(fusion);

        // Audio & Video Streaming
        fusion.AddClient<ILiveAudioStreams>();
        fusion.AddClient<ILiveVideoStreams>();
        fusion.AddClient<ILiveSessions>();

        // Chat
        fusion.AddClient<IChats>();
        fusion.AddClient<IAuthors>();
        fusion.AddClient<IRoles>();
        fusion.AddClient<IMentions>();
        fusion.AddClient<IReactions>();
        fusion.AddClient<ISharedLocations>();
        fusion.AddClient<IPlaces>();
        fusion.AddClient<ITranslations>();
        fusion.AddClient<IChatThreads>();
        fusion.AddClient<IDiagnostics>();

        // Search
        fusion.AddClient<ISearch>();

        // Contacts
        fusion.AddClient<IContacts>();
        fusion.AddClient<IExternalContacts>();
        fusion.AddClient<IExternalContactHashes>();

        // Invite
        fusion.AddClient<IInvites>();

        // Media
        fusion.AddClient<IMediaLinkPreviews>();
        fusion.AddClient<IUploads>();
        fusion.AddClient<IMedia>("IMedias"); // TODO(AY): Remove "IMedias"
        rpc.AddClient<IGifs>();

        // Notification
        fusion.AddClient<INotifications>();

        // Aliases
        fusion.AddClient<IAliases>();

        // Conversations
        fusion.AddClient<IConversations>();

        // Users
        fusion.AddClient<ISessionTemporals>();
        fusion.AddClient<IUserSettings>();
        fusion.AddClient<IServerKvas>();
        fusion.AddClient<IAccounts>();
        fusion.AddClient<IAvatars>();
        fusion.AddClient<IUserPresences>();
        fusion.AddClient<IChatPositions>();
        fusion.AddClient<IChatUsages>();

        // Auth-related
        rpc.AddClient<ISecureTokens>();
        fusion.AddClient<IMobileSessions>();
        if (HostInfo.HostKind.IsMauiApp())
            fusion.AddClient<INativeAuth>();
        fusion.AddClient<IPhoneAuth>();
        fusion.AddClient<IEmailAuth>();

        // Misc.
        rpc.AddClient<ICaptcha>();
        fusion.AddClient<ISystemProperties>();
        fusion.AddClient<ITimeZones>();
        fusion.AddClient<IPhones>();
        fusion.AddClient<IEmails>();
    }

    public void ConfigureFusionClients(FusionBuilder fusion)
    {
        var hostKind = HostInfo.HostKind;
        var useRpcSwitchingClient = Constants.Rpc.UseHttpClient; // HostInfo.HostKind is HostKind.MauiApp
        fusion.Rpc.AddWebSocketClient(c => {
            var options = RpcWebSocketClientOptions.Default with {
                ConnectionUriResolver = peer => {
                    if (peer.Ref != RpcRef.Default)
                        throw StandardError.Internal("Client-side RpcPeer.Ref != RpcRef.Default.");

                    var client = c.GetRequiredService<RpcWebSocketClient>();
                    var settings = client.Options;
                    var urlMapper = client.Services.UrlMapper();
                    var sb = ActualLab.Text.StringBuilderExt.Acquire();
                    sb.Append(urlMapper.WebsocketBaseUrl);
                    sb.Append(settings.RequestPath);
                    sb.Append('?');
                    sb.Append(settings.ClientIdParameterName);
                    sb.Append('=');
                    sb.Append(peer.ClientId); // Always Url-encoded
                    sb.Append('&');
                    sb.Append(settings.SerializationFormatParameterName);
                    sb.Append('=');
                    sb.Append(peer.SerializationFormat.Key);
                    return sb.ToStringAndRelease().ToUri();
                },
            };
            if (hostKind.IsMauiApp())
                // NOTE(AY): "new ClientWebSocket()" triggers this exception in WASM:
                // - PlatformNotSupportedException: Operation is not supported on this platform.
                // So the code below should never run in WASM.
                options = options with {
                    WebSocketOwnerFactory = peer => {
                        var ws = new ClientWebSocket();
                        var gclbCookieHeader = AppLoadBalancerSettings.Instance.GclbCookieHeader;
                        ws.Options.SetRequestHeader(gclbCookieHeader.Name, gclbCookieHeader.Value);
                        if (c.GetService<TrueSessionResolver>() is { HasSession: true } trueSessionResolver)
                            ws.Options.SetRequestHeader(Constants.Session.HeaderName, trueSessionResolver.Session.Id);
                        if (Constants.Rpc.Compression.IsClientSideEnabled)
                            ws.Options.DangerousDeflateOptions = new WebSocketDeflateOptions();
                        if (HostNameRemapper.Instance is not { } hostNameRemapper)
                            return new WebSocketOwner(peer.Ref.Address, ws, c);

                        // HostNameRemapper changes the host name of the WebSocket connection,
                        // and since it's an SSL connection, plain change in the original URL
                        // will trigger SSL certificate validation failure.
                        // SocketsHttpHandler's ConnectCallback is the right place to make this change.
                        var handler = new SocketsHttpHandler {
                            ConnectCallback = async (context, cancellationToken) => {
                                var host = context.DnsEndPoint.Host;
                                var port = context.DnsEndPoint.Port;
                                var remapped = hostNameRemapper.Get(host);
                                var endPoint = IPAddress.TryParse(remapped, out var ip)
                                    ? (EndPoint)new IPEndPoint(ip, port)
                                    : new DnsEndPoint(remapped, port);
                                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                                try {
                                    await socket.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
                                    return new NetworkStream(socket, ownsSocket: true);
                                }
                                catch {
                                    socket.Dispose();
                                    throw;
                                }
                            },
                        };
                        return new WebSocketOwner(peer.Ref.Address, ws, c) { Handler = handler };
                    },
                };
            return options;
        });
        if (useRpcSwitchingClient)
            fusion.Rpc.AddHttpClient(c => RpcHttpClientOptions.Default with {
                ConnectionUriResolver = peer => {
                    if (peer.Ref != RpcRef.Default)
                        throw StandardError.Internal("Client-side RpcPeer.Ref != RpcRef.Default.");

                    var client = c.GetRequiredService<RpcHttpClient>();
                    var settings = client.Options;
                    var urlMapper = client.Services.UrlMapper();
                    var sb = ActualLab.Text.StringBuilderExt.Acquire();
                    sb.Append(urlMapper.BaseUrl.TrimSuffix("/"));
                    sb.Append(settings.RequestPath);
                    sb.Append('?');
                    sb.Append(settings.ClientIdParameterName);
                    sb.Append('=');
                    sb.Append(peer.ClientId); // Always Url-encoded
                    sb.Append('&');
                    sb.Append(settings.SerializationFormatParameterName);
                    sb.Append('=');
                    sb.Append(peer.SerializationFormat.Key);
                    return sb.ToStringAndRelease().ToUri();
                },
                HttpClientFactory = _ => {
                    var handler = new SocketsHttpHandler {
                        EnableMultipleHttp2Connections = true,
                    };
                    if (HostNameRemapper.Instance is { } hostNameRemapper)
                        handler.ConnectCallback = async (context, ct) => {
                            var host = context.DnsEndPoint.Host;
                            var port = context.DnsEndPoint.Port;
                            var remapped = hostNameRemapper.Get(host);
                            var endPoint = IPAddress.TryParse(remapped, out var ip)
                                ? (EndPoint)new IPEndPoint(ip, port)
                                : new DnsEndPoint(remapped, port);
                            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                            try {
                                await socket.ConnectAsync(endPoint, ct).ConfigureAwait(false);
                                return new NetworkStream(socket, ownsSocket: true);
                            }
                            catch {
                                socket.Dispose();
                                throw;
                            }
                        };
                    var sessionResolver = c.GetService<TrueSessionResolver>();
                    var sessionHandler = new RpcHttpSessionHeaderHandler(sessionResolver) { InnerHandler = handler };
                    var httpClient = new HttpClient(sessionHandler, disposeHandler: true);
                    var gclbCookieHeader = AppLoadBalancerSettings.Instance.GclbCookieHeader;
                    httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                        gclbCookieHeader.Name, gclbCookieHeader.Value);
                    return httpClient;
                },
            });

        if (useRpcSwitchingClient) {
            var services = fusion.Services;
            services.RemoveAll(d => d.ServiceType == typeof(RpcClient));
            services.AddSingleton(c => {
                var rpcWebSocketClient = c.GetRequiredService<RpcWebSocketClient>();
                var rpcHttpClient = c.GetRequiredService<RpcHttpClient>();
                return new RpcSwitchingClient(c, rpcWebSocketClient, rpcHttpClient) {
                    StartPromoted = Constants.Rpc.UseHttpClient,
                };
            });
            services.AddAlias<RpcClient, RpcSwitchingClient>();
        }

        var restEase = fusion.Services.AddRestEase();
        restEase.ConfigureHttpClient((c, name, o) => {
            var urlMapper = c.UrlMapper();
            var clientBaseUrl = urlMapper.ApiBaseUrl.ToUri();
            o.HttpClientActions.Add(client => {
                client.BaseAddress = clientBaseUrl;
                client.DefaultRequestVersion = HttpVersion.Version30;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
                // c.LogFor(typeof(AppStartup)).LogInformation(
                //     "HTTP client '{Name}' configured @ {BaseAddress}", name, client.BaseAddress);
                if (!hostKind.IsMauiApp())
                    return;

                var gclbCookieHeader = AppLoadBalancerSettings.Instance.GclbCookieHeader;
                client.DefaultRequestHeaders.Add(gclbCookieHeader.Name, gclbCookieHeader.Value);
                if (c.GetService<TrueSessionResolver>() is { HasSession: true } trueSessionResolver) {
                    var session = trueSessionResolver.Session;
                    client.DefaultRequestHeaders.Add(Constants.Session.HeaderName, session.Id);
                }
            });
            if (hostKind.IsMauiApp())
                o.HttpMessageHandlerBuilderActions.Add(b => {
                    if (b.PrimaryHandler is HttpClientHandler h)
                        h.UseCookies = false;
                });
        });
    }

    // Nested types

    private sealed class RpcHttpSessionHeaderHandler(TrueSessionResolver? sessionResolver) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (sessionResolver is { HasSession: true } r)
                request.Headers.TryAddWithoutValidation(Constants.Session.HeaderName, r.Session.Id);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
