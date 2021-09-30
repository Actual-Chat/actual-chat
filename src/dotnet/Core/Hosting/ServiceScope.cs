namespace ActualChat.Hosting;

public static class ServiceScope
{
    public static readonly Symbol Server = nameof(Server);
    public static readonly Symbol Client = nameof(Client);
    public static readonly Symbol BlazorUI = nameof(BlazorUI);
}
