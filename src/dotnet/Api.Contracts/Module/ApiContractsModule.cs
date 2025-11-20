using System.Net;
using System.Net.WebSockets;
using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Hosting;
using ActualChat.Invite;
using ActualChat.Kvas;
using ActualChat.Media;
using ActualChat.MLSearch;
using ActualChat.Notification;
using ActualChat.Roulette;
using ActualChat.Search;
using ActualChat.Security;
using ActualChat.Streaming;
using ActualChat.Users;
using ActualLab.RestEase;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;
using ActualLab.Rpc.WebSockets;

namespace ActualChat.Module;

#pragma warning disable IL2026 // Fine for modules

public sealed class ApiContractsModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IAppModule
{
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "Fine for Fusion")]
    protected override void InjectServices(IServiceCollection services)
    {
        var fusion = services.AddFusion().AddAuthClient();
        var rpc = fusion.Rpc;

        // Fusion & RestEase client
        ConfigureFusionClients(fusion);

        // Audio
        rpc.AddClient<IStreamServer>();
        services.AddSingleton<IStreamClient>(c => new StreamClient(c));
        services.AddSingleton<AudioDownloader>(c => new HttpClientAudioDownloader(c));

        // Chat
        fusion.AddClient<IChats>();
        fusion.AddClient<IAuthors>();
        fusion.AddClient<IRoles>();
        fusion.AddClient<IMentions>();
        fusion.AddClient<IReactions>();
        fusion.AddClient<IPlaces>();
        fusion.AddClient<ITranslations>();
        fusion.AddClient<IChatThreads>();

        // Search
        fusion.AddClient<ISearch>();
        fusion.AddClient<IMLSearch>();

        // Contacts
        fusion.AddClient<IContacts>();
        fusion.AddClient<IExternalContacts>();
        fusion.AddClient<IExternalContactHashes>();

        // Invite
        fusion.AddClient<IInvites>();

        // Media
        fusion.AddClient<IMediaLinkPreviews>();

        // Notification
        fusion.AddClient<INotifications>();

        // Aliases
        fusion.AddClient<IAliases>();

        // Chat Roulette
        fusion.AddClient<IRoulette>();
        fusion.AddClient<IRouletteProfiles>();

        // Conversations
        fusion.AddClient<IConversations>();

        // Users
        rpc.AddClient<ISecureTokens>();
        fusion.AddClient<ISystemProperties>();
        fusion.AddClient<IMobileSessions>();
        if (HostInfo.HostKind.IsMauiApp())
            services.AddRestEase(restEase => restEase.AddClient<INativeAuthClient>());
        fusion.AddClient<IServerKvas>();
        fusion.AddClient<IServerSettings>();
        fusion.AddClient<IAccounts>();
        fusion.AddClient<IAvatars>();
        fusion.AddClient<IUserPresences>();
        fusion.AddClient<IChatPositions>();
        fusion.AddClient<IChatUsages>();
        fusion.AddClient<IPhoneAuth>();
        fusion.AddClient<IPhones>();
        fusion.AddClient<IEmails>();
        fusion.AddClient<IEmailAuth>();
        fusion.AddClient<ITimeZones>();
        rpc.AddClient<ICaptcha>();
    }

    public void ConfigureFusionClients(FusionBuilder fusion)
    {
        var hostKind = HostInfo.HostKind;
        fusion.Rpc.AddWebSocketClient(c => {
            var options = RpcWebSocketClientOptions.Default with {
                ConnectionUriResolver = peer => {
                    if (peer.Ref != RpcPeerRef.Default)
                        throw StandardError.Internal("Client-side RpcPeer.Ref != RpcPeerRef.Default.");

                    var client = peer.Hub.Services.GetRequiredService<RpcWebSocketClient>();
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
                        return new WebSocketOwner(peer.Ref.Address, ws, peer.Hub.Services);
#if false
                        // Non-native Android WebSocket stack requires SocketsHttpHandler to support TLS 1.2
                        var handler = new SocketsHttpHandler() {
                            SslOptions = new SslClientAuthenticationOptions() {
                                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            },
                        };
                        return new WebSocketOwner(peer.Ref.Address, ws, peer.Hub.Services) { Handler = handler };
#endif
                    },
                };
            return options;
        });

        var restEase = fusion.Services.AddRestEase();
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
}
