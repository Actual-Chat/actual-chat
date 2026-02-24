namespace ActualChat.UI.App.Services;

public sealed record AppServerInstance(string HostName)
{
    public static readonly AppServerInstance Prod = new (Constants.Hosts.Voxt);
    public static readonly AppServerInstance Dev = new (Constants.Hosts.DevVoxt);

    public static AppServerInstance? TryCreate(string hostName)
    {
        if (hostName.IsNullOrEmpty())
            return null;
        if (OrdinalIgnoreCaseEquals(Prod.HostName, hostName))
            return Prod;
        if (OrdinalIgnoreCaseEquals(Dev.HostName, hostName))
            return Dev;

        var hostType = Uri.CheckHostName(hostName);
        return hostType != UriHostNameType.Dns ? null
            : new AppServerInstance(hostName);
    }
}
