using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.WebSockets;
using ActualChat.Audio.Module;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Audio.UI.Blazor.Pages;
using ActualChat.Chat.Module;
using ActualChat.Chat.UI.Blazor.Components;
using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Chat.UI.Blazor.Pages;
using ActualChat.Chat.UI.Blazor.Testing;
using ActualChat.Contacts.Module;
using ActualChat.Contacts.UI.Blazor.Module;
using ActualChat.Contacts.UI.Blazor.Services.StlInterceptionProxies;
using ActualChat.Diff.Handlers;
using ActualChat.Feedback.Module;
using ActualChat.Hosting;
using ActualChat.Invite.Module;
using ActualChat.Media.Module;
using ActualChat.MediaPlayback.Module;
using ActualChat.Module;
using ActualChat.Notification.Module;
using ActualChat.Notification.UI.Blazor.Module;
using ActualChat.Security;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Pages;
using ActualChat.UI.Blazor.App.Pages.Landing.Docs;
using ActualChat.UI.Blazor.App.Pages.Test;
using ActualChat.UI.Blazor.Components.Requirements;
using ActualChat.UI.Blazor.Module;
using ActualChat.UI.Blazor.Pages;
using ActualChat.UI.Blazor.Pages.ComputeStateTestPage;
using ActualChat.UI.Blazor.Pages.DiveInModalTestPage;
using ActualChat.UI.Blazor.Pages.ErrorBarrierTestPage;
using ActualChat.UI.Blazor.Pages.RenderSlotTestPage;
using ActualChat.Users.Module;
using ActualChat.Users.UI.Blazor.Module;
using ActualChat.Users.UI.Blazor.Pages;
using Cysharp.Text;
using Stl.Interception.Interceptors;
using Stl.RestEase;
using Stl.Rpc;
using Stl.Rpc.Clients;

namespace ActualChat.UI.Blazor.App;

#pragma warning disable IL2026 // Fine for module-like code

public static class AppStartup
{
    // Stl.Interception, Stl.Rpc, Stl.CommandR, Stl.Fusion dependencies are referenced
    // by [DynamicDependency] on FusionBuilder from v6.7.2.
    // Libraries
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PriorityQueue<,>))] // MemoryPack uses it
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Range<>))] // JS dependency
    // Diffs
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MissingDiffHandler<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CloneDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NullableDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RecordDiffHandler<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(OptionDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SetDiffHandler<,>))]
    // Pages
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(HomePage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UnavailablePage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UserPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChatPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(EmbeddedChatPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChatInvitePage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UserInvitePage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DocsCookiesPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DocsFaqPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DocsPrivacyPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DocsTermsPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AdminUserInvitesPage))]
    // Test pages
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(VirtualListTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(VirtualListTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AudioPlayerTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AudioRecorderTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RenderSlotTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UIColorsTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ErrorBarrierTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ErrorToastTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(InfoToastTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DiveInModalTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ReconnectOverlayTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(LoadingOverlayTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MicPermissionTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SkeletonsTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RequirementsTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(FeaturesTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ComputeStateTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MarkupEditorTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AuthTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(BlazorTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(JSTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(EmbeddedTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TotpTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiTestPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SystemTestPage))]
    public static void ConfigureServices(
        IServiceCollection services,
        AppKind appKind,
        Func<IServiceProvider, HostModule[]>? platformModuleFactory = null)
    {
#if !DEBUG
        InterceptorBase.Options.Defaults.IsValidationEnabled = false;
#else
        if (appKind.IsMauiApp())
            InterceptorBase.Options.Defaults.IsValidationEnabled = false;
#endif
        var tracer = Tracer.Default;

        // Fusion services
        var fusion = services.AddFusion();
        var restEase = services.AddRestEase();
        restEase.ConfigureHttpClient((c, name, o) => {
            var urlMapper = c.GetRequiredService<UrlMapper>();
            var clientBaseUrl = urlMapper.ApiBaseUrl.ToUri();
            o.HttpClientActions.Add(client => {
                client.BaseAddress = clientBaseUrl;
                client.DefaultRequestVersion = OSInfo.IsAndroid
                    ? HttpVersion.Version20
                    : HttpVersion.Version30;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
                // c.LogFor(typeof(AppStartup)).LogInformation(
                //     "HTTP client '{Name}' configured @ {BaseAddress}", name, client.BaseAddress);
                if (!appKind.IsMauiApp())
                    return;

                var gclbCookieHeader = AppLoadBalancerSettings.Instance.GclbCookieHeader;
                client.DefaultRequestHeaders.Add(gclbCookieHeader.Name, gclbCookieHeader.Value);
                if (c.GetService<TrueSessionResolver>() is { HasSession: true } trueSessionResolver) {
                    var session = trueSessionResolver.Session;
                    client.DefaultRequestHeaders.Add(Constants.Session.HeaderName, session.Id.Value);
                }
            });
            if (appKind.IsMauiApp())
                o.HttpMessageHandlerBuilderActions.Add(b => {
                    if (b.PrimaryHandler is HttpClientHandler h)
                        h.UseCookies = false;
                });
        });

        fusion.Rpc.AddWebSocketClient(c => new RpcWebSocketClient.Options() {
            ConnectionUriResolver = (client, peer) => {
                var settings = client.Settings;
                var urlMapper = client.Services.GetRequiredService<UrlMapper>();

                var sb = StringBuilderExt.Acquire();
                if (peer.Ref == RpcPeerRef.Default)
                    sb.Append(urlMapper.WebsocketBaseUrl);
                else {
                    var addressAndPort = peer.Ref.Key.Value;
                    sb.Append(addressAndPort.OrdinalEndsWith(":443") ? "wss://" : "ws://");
                    sb.Append(addressAndPort);
                }
                sb.Append(settings.RequestPath);
                sb.Append('?');
                sb.Append(settings.ClientIdParameterName);
                sb.Append('=');
                sb.Append(client.ClientId.UrlEncode());
                var uri = sb.ToStringAndRelease().ToUri();
                c.LogFor(peer.GetType()).LogInformation("Connection Url: {Url}", uri);
                return uri;
            },
        });
        if (appKind.IsMauiApp())
            services.AddTransient<ClientWebSocket>(c => {
                // NOTE(AY): "new ClientWebSocket()" triggers this exception in WASM:
                // - PlatformNotSupportedException: Operation is not supported on this platform.
                // So the code below should never run in WASM.
                var ws = new ClientWebSocket();
                var gclbCookieHeader = AppLoadBalancerSettings.Instance.GclbCookieHeader;
                ws.Options.SetRequestHeader(gclbCookieHeader.Name, gclbCookieHeader.Value);
                if (c.GetService<TrueSessionResolver>() is { HasSession: true } trueSessionResolver)
                    ws.Options.SetRequestHeader(Constants.Session.HeaderName, trueSessionResolver.Session.Id.Value);
                return ws;
            });

        // Creating modules
        using var _ = tracer.Region($"{nameof(ModuleHostBuilder)}.{nameof(ModuleHostBuilder.Build)}");
        var moduleServices = services.BuildServiceProvider();
        var moduleHostBuilder = new ModuleHostBuilder()
            // From less dependent to more dependent!
            .WithModules(
                // Core modules
                new CoreModule(moduleServices),
                // Generic modules
                new MediaPlaybackModule(moduleServices),
                // Service-specific & service client modules
                new AudioClientModule(moduleServices),
                new FeedbackClientModule(moduleServices),
                new UsersContractsModule(moduleServices),
                new UsersClientModule(moduleServices),
                new ContactsClientModule(moduleServices),
                new ChatModule(moduleServices),
                new ChatClientModule(moduleServices),
                new MediaClientModule(moduleServices),
                new InviteClientModule(moduleServices),
                new NotificationClientModule(moduleServices),
                // UI modules
                new BlazorUICoreModule(moduleServices),
                new AudioBlazorUIModule(moduleServices),
                new UsersBlazorUIModule(moduleServices),
                new ContactsBlazorUIModule(moduleServices),
                new ChatBlazorUIModule(moduleServices),
                new NotificationBlazorUIModule(moduleServices),
                // This module should be the last one
                new BlazorUIAppModule(moduleServices)
            );
        if (platformModuleFactory != null)
            moduleHostBuilder = moduleHostBuilder.WithModules(platformModuleFactory.Invoke(moduleServices));
        moduleHostBuilder.Build(services);
    }
}
