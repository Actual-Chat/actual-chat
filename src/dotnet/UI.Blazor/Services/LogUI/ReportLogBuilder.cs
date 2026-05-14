using System.Text;
using ActualChat.Hosting;

namespace ActualChat.UI.Blazor.Services;

public static class ReportLogBuilder
{
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public static byte[] BuildHeader(HostInfo hostInfo, AccountFull? account, Moment generatedAt)
    {
        var sb = new StringBuilder(1024);
        sb.Append("=== ").Append(CoreConstants.AppName).Append(" log report ===").Append('\n');
        sb.Append("Generated:    ").Append(generatedAt.ToString(TimestampFormat)).Append('\n');
        sb.Append("App:          ")
            .Append("HostKind=").Append(hostInfo.HostKind)
            .Append(", AppKind=").Append(hostInfo.AppKind)
            .Append(", Environment=").Append(hostInfo.Environment.Value)
            .Append(", DeviceModel=").Append(hostInfo.DeviceModel)
            .Append('\n');
        sb.Append("BaseUrl:      ").Append(hostInfo.BaseUrl).Append('\n');
        sb.Append("Account:      ");
        if (account is { } a && a.HasId())
            sb.Append(a.Id.Value).Append("  ").Append(a.Name);
        else
            sb.Append("(none)");
        sb.Append('\n').Append('\n');
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
