namespace ActualChat.Hosting;

public static class ServiceScope
{
    public static Symbol Server { get; } = nameof(Server);
    public static Symbol Client { get; } = nameof(Client);
    public static Symbol BlazorUI { get; } = nameof(BlazorUI);
    public static Symbol Test { get; } = nameof(Test);

    public static Symbol WebServerApp { get; } = nameof(WebServerApp);
    public static Symbol WasmApp { get; } = nameof(WasmApp);
    public static Symbol MauiApp { get; } = nameof(MauiApp);
}
