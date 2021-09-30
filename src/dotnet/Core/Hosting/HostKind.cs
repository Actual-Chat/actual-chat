namespace ActualChat.Hosting;

public static class HostKind
{
    public static readonly Symbol WebServer = nameof(WebServer);
    public static readonly Symbol Blazor = nameof(Blazor);
    public static readonly Symbol Test = nameof(Test);
}
