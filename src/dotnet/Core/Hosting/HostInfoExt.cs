namespace ActualChat.Hosting;

public static class HostInfoExt
{
    public static bool IsMobileMauiApp(this HostInfo hostInfo)
        => hostInfo.AppKind.IsMauiApp() && hostInfo.ClientKind.IsMobile();
}
