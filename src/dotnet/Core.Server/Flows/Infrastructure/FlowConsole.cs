using System.Text;

namespace ActualChat.Flows.Infrastructure;

public sealed class FlowConsole(IFlowImpl flow, string prefix = "")
{
    private const int InitialCapacity = 128;
    private const int MaxStoredContentLength = 8192;
    public const string LogSectionFormat = "---- {1} at {0:yy-MM-dd HH:mm:ss.fff} ----\n";
    public const string LogFormat = "{0:F2} [{1}] {2}\n";
    public const string LogErrorFormat = "{0:F2} [{1}] {2}\n| {3}: {4}\n";
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
        Suffix.AppendFormat(CultureInfo.InvariantCulture, LogSectionFormat, DateTime.Now, section);
        if (flow.Runtime?.Log is { } log)
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            log.LogInformation("`{FlowId}`: {Section}", flow.Id, section);
        return this;
    }

    public FlowConsole Log(string message)
        => Log(LogLevel.Information, message);

    public FlowConsole LogWarning(string message, Exception? error = null)
        => Log(LogLevel.Warning, message, error);

    public FlowConsole LogError(string message, Exception? error = null)
        => Log(LogLevel.Error, message, error);

    public FlowConsole Log(LogLevel level, string message, Exception? error = null)
    {
        var cLevel = GetLogLevelChar(level);
        if (cLevel is not ' ') {
            if (error is not null)
                Suffix.AppendFormat(CultureInfo.InvariantCulture, LogErrorFormat,
                    CreatedAt.Elapsed.TotalSeconds, cLevel, message, error.GetType().GetName(), error.Message);
            else
                Suffix.AppendFormat(CultureInfo.InvariantCulture, LogFormat,
                    CreatedAt.Elapsed.TotalSeconds, cLevel, message);
            if (flow.Runtime?.Log is { } log)
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                log.Log(level, error, "`{FlowId}`: {Message}", flow.Id, message);
        }
        return this;
    }

    public FlowConsole Commit()
    {
        Prefix = ToString();
        _suffix = null;
        return this;
    }

    // Private methods

    private static char GetLogLevelChar(LogLevel level)
        => level switch {
            LogLevel.Trace => 'T',
            LogLevel.Debug => 'D',
            LogLevel.Information => 'I',
            LogLevel.Warning => 'W',
            LogLevel.Error => 'E',
            LogLevel.Critical => '!',
            _ => ' ',
        };
}
