namespace ActualChat.Hosting;

public static class ServiceScope
{
    public static Symbol Server { get; } = nameof(Server);
    public static Symbol Client { get; } = nameof(Client);
    public static Symbol BlazorUI { get; } = nameof(BlazorUI);
}
