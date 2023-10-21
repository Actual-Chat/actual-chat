namespace ActualChat.Hosting;

public static class ServiceScope
{
    public static readonly Symbol Server = nameof(Server);
    public static readonly Symbol Client = nameof(Client);
    public static readonly Symbol BlazorUI = nameof(BlazorUI);
    public static readonly Symbol Test = nameof(Test);

    public static readonly Symbol WebServerApp = nameof(WebServerApp);
    public static readonly Symbol WasmApp = nameof(WasmApp);
    public static readonly Symbol MauiApp = nameof(MauiApp);
}
