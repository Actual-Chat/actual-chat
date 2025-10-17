using System.Text;

namespace ActualChat.Flows.Infrastructure;

public sealed class FlowConsole(string prefix = "")
{
    private const int InitialCapacity = 128;
    private const int MaxStoredContentLength = 8192;
    public const string LogMessageFormat = "{1:MM-dd HH:mm:ss.fff} {0}\n";
    public const char NewLine = '\n';

    private StringBuilder? _suffix;

    public string Prefix { get; private set; } = prefix;
    public StringBuilder Suffix => _suffix ??= new StringBuilder(InitialCapacity);

    public override string ToString()
        => _suffix is null || _suffix.Length == 0
            ? Prefix
            : _suffix.GetSuffix(Prefix, MaxStoredContentLength);

    public FlowConsole WriteLine(string message)
    {
        Suffix.Append(message).Append(NewLine);
        return this;
    }

    public FlowConsole Log(string message)
    {
        Suffix.AppendFormat(CultureInfo.InvariantCulture, LogMessageFormat, message, DateTime.Now);
        return this;
    }

    public void Commit()
    {
        Prefix = ToString();
        _suffix = null;
    }
}
