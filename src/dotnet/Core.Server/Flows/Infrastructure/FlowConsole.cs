using System.Text;

namespace ActualChat.Flows.Infrastructure;

public sealed class FlowConsole(string prefix = "")
{
    private const int InitialCapacity = 128;
    private const int MaxStoredContentLength = 8192;
    public const string LogSectionMessageFormat = "{0} at {1:yy-MM-dd HH:mm:ss.fff}\n";
    public const string LogMessageFormat = "{1:F2} {0}\n";
    public const char NewLine = '\n';

    private StringBuilder? _suffix;

    public string Prefix { get; private set; } = prefix;
    public StringBuilder Suffix => _suffix ??= new StringBuilder(InitialCapacity);
    public CpuTimestamp CreatedAt { get; } = CpuTimestamp.Now;

    public override string ToString()
        => _suffix is null || _suffix.Length == 0
            ? Prefix
            : _suffix.GetSuffix(Prefix, MaxStoredContentLength);

    public FlowConsole WriteLine(string message)
    {
        Suffix.Append(message).Append(NewLine);
        return this;
    }

    public FlowConsole LogSection(string section)
    {
        Suffix.AppendFormat(CultureInfo.InvariantCulture, LogSectionMessageFormat, section, DateTime.Now);
        return this;
    }

    public FlowConsole Log(string message)
    {
        Suffix.AppendFormat(CultureInfo.InvariantCulture, LogMessageFormat, message, CreatedAt.Elapsed.TotalSeconds);
        return this;
    }

    public FlowConsole Commit()
    {
        Prefix = ToString();
        _suffix = null;
        return this;
    }
}
