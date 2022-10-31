namespace ActualChat.Hosting;

public static class AppKindExt
{
    public static ImmutableHashSet<Symbol> GetRequiredServiceScopes(this AppKind appKind)
        => appKind switch {
            AppKind.WebServer => ImmutableHashSet.Create(
                ServiceScope.WebServerApp,
                ServiceScope.Server,
                ServiceScope.BlazorUI),
            AppKind.Wasm => ImmutableHashSet.Create(
                ServiceScope.WasmApp,
                ServiceScope.Client,
                ServiceScope.BlazorUI),
            AppKind.Maui => ImmutableHashSet.Create(
                ServiceScope.MauiApp,
                ServiceScope.Client,
                ServiceScope.BlazorUI),
            AppKind.Test => AppKind.Test.GetRequiredServiceScopes().Add(ServiceScope.Test),
            _ => throw new ArgumentOutOfRangeException(nameof(appKind), appKind, null),
        };
}
