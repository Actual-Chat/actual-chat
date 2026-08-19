using System.Reflection;

namespace ActualChat.Benchmarks;

public enum MarkupSampleKind
{
    PlainShort,
    PlainEnglish,
    PlainRussian,
    Regular,
    Table,
    Long,
    VideoStats,
    VideoStatsFenced,
    ConsoleLog,
    ConsoleLogFenced,
}

/// <summary>
/// Parser input, ordered by how often Voxt sees it: real messages without markup, then real
/// messages with it, then pasted logs - the last both as-is and inside a code fence.
/// </summary>
public static class MarkupSamples
{
    private static readonly Dictionary<MarkupSampleKind, string[]> Samples = Build();

    public static string[] Get(MarkupSampleKind kind)
        => Samples[kind];

    public static int GetLength(MarkupSampleKind kind)
        => Samples[kind].Sum(x => x.Length);

    // Private methods

    private static Dictionary<MarkupSampleKind, string[]> Build()
    {
        var videoStats = Read("video-stats.txt");
        var consoleLog = Read("console-log.txt");
        return new Dictionary<MarkupSampleKind, string[]> {
            { MarkupSampleKind.PlainShort, ReadLines("plain-messages.txt") },
            { MarkupSampleKind.PlainEnglish, [Read("plain-english.txt")] },
            { MarkupSampleKind.PlainRussian, [Read("plain-russian.txt")] },
            { MarkupSampleKind.Regular, [Read("regular-message.txt")] },
            { MarkupSampleKind.Table, [Read("table-message.txt")] },
            { MarkupSampleKind.Long, [Read("long-message.txt")] },
            { MarkupSampleKind.VideoStats, [videoStats] },
            { MarkupSampleKind.VideoStatsFenced, [Fence(videoStats)] },
            { MarkupSampleKind.ConsoleLog, [consoleLog] },
            { MarkupSampleKind.ConsoleLogFenced, [Fence(consoleLog)] },
        };
    }

    private static string Fence(string text)
        => "```\n" + text + "\n```";

    private static string[] ReadLines(string resourceName)
        => Read(resourceName).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static string Read(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' is not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n").Trim();
    }
}
