namespace ActualChat.Hosting;

public static class HostKind
{
    public static Symbol WebServer { get; } = nameof(WebServer);
    public static Symbol Blazor { get; } = nameof(Blazor);
    public static Symbol Maui { get; } = nameof(Maui);
    public static Symbol Test { get; } = nameof(Test);
}
