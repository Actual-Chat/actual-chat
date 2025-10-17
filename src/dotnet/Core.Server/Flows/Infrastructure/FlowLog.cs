using System.Text;

namespace ActualChat.Flows.Infrastructure;

public static class FlowLog
{
    private const int InitialCapacity = 256;
    private const int MaxStoredSuffixLength = 8192;

    public static StringBuilder New(string messages)
    {
        var log = new StringBuilder(Math.Min(InitialCapacity, messages.Length));
        return log.Append(messages);
    }

    public static string GetStoredSuffix(StringBuilder log)
        => log.GetSuffix(MaxStoredSuffixLength);
}
