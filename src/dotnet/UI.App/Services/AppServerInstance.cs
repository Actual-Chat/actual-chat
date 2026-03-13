namespace ActualChat.UI.App.Services;

public sealed record AppServerInstance(string HostName)
{
    public static readonly AppServerInstance Prod = new (Constants.Hosts.Voxt);
    public static readonly AppServerInstance Dev = new (Constants.Hosts.DevVoxt);

    public static AppServerInstance? TryCreate(string hostName)
    {
        if (hostName.IsNullOrEmpty())
            return null;
        if (string.Equals(Prod.HostName, hostName, StringComparison.OrdinalIgnoreCase))
            return Prod;
        if (string.Equals(Dev.HostName, hostName, StringComparison.OrdinalIgnoreCase))
            return Dev;

        var hostType = Uri.CheckHostName(hostName);
        return hostType != UriHostNameType.Dns ? null
            : new AppServerInstance(hostName);
    }
}
