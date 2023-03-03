namespace ActualChat.Hosting;

public static class AppKindExt
{
    public static bool IsServer(this AppKind appKind)
        => appKind is AppKind.WebServer or AppKind.TestServer;
    public static bool IsWebServer(this AppKind appKind)
        => appKind is AppKind.WebServer;
    public static bool IsTestServer(this AppKind appKind)
        => appKind is AppKind.TestServer;

    public static bool IsClient(this AppKind appKind)
        => appKind is AppKind.WasmApp or AppKind.MauiApp;
    public static bool IsWasmApp(this AppKind appKind)
        => appKind is AppKind.WasmApp;
    public static bool IsMauiApp(this AppKind appKind)
        => appKind is AppKind.MauiApp;

    public static bool HasBlazorUI(this AppKind appKind)
        => appKind is not AppKind.Unknown;

    public static ImmutableHashSet<Symbol> GetRequiredServiceScopes(this AppKind appKind)
        => appKind switch {
            AppKind.WebServer => ImmutableHashSet.Create(
                ServiceScope.WebServerApp,
                ServiceScope.Server,
                ServiceScope.BlazorUI),
            AppKind.WasmApp => ImmutableHashSet.Create(
                ServiceScope.WasmApp,
                ServiceScope.Client,
                ServiceScope.BlazorUI),
            AppKind.MauiApp => ImmutableHashSet.Create(
                ServiceScope.MauiApp,
                ServiceScope.Client,
                ServiceScope.BlazorUI),
            AppKind.TestServer => AppKind.WebServer.GetRequiredServiceScopes()
                .Add(ServiceScope.Test),
            _ => throw new ArgumentOutOfRangeException(nameof(appKind), appKind, null),
        };
}
