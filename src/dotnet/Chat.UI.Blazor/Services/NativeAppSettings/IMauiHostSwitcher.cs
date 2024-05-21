namespace ActualChat.Chat.UI.Blazor.Services;

public interface IMauiHostSwitcher
{
    MauiHost DefaultHost { get; }
    MauiHost CurrentHost { get; }
    MauiHost GetHost();
    void SetHost(MauiHost host);
}

public record MauiHost(string Host)
{
    public static readonly MauiHost Prod = new (Constants.Hosts.ActualChat);
    public static readonly MauiHost Dev = new (Constants.Hosts.DevActualChat);
    public bool IsWellKnown => OrdinalIgnoreCaseEquals(Host, Prod.Host) || OrdinalIgnoreCaseEquals(Host, Dev.Host);
    public bool IsCustom => !IsWellKnown;

    public static MauiHost? TryCreate(string host)
    {
        if (host.IsNullOrEmpty())
            return null;

        if (OrdinalIgnoreCaseEquals(Prod.Host, host))
            return Prod;
        
        if (OrdinalIgnoreCaseEquals(Dev.Host, host))
            return Dev;

        var hostType = Uri.CheckHostName(host);
        if (hostType != UriHostNameType.Dns)
            return null;

        return new MauiHost(host);
    }
}
