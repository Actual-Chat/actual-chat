using System.Reflection;

namespace ActualChat.Benchmarks;

public enum MarkupSampleKind
{
    Regular,
    Table,
    Long,
}

/// <summary>
/// Real Voxt messages used as parser input: <see cref="MarkupSampleKind.Regular"/> is a few
/// sentences with one mention and one bold span, <see cref="MarkupSampleKind.Table"/> adds a
/// 7-row table, and <see cref="MarkupSampleKind.Long"/> is a ~13K char review with headers,
/// lists, code blocks, links and inline styles.
/// </summary>
public static class MarkupSamples
{
    private static readonly Dictionary<MarkupSampleKind, string> Texts = new() {
        { MarkupSampleKind.Regular, Read("regular-message.txt") },
        { MarkupSampleKind.Table, Read("table-message.txt") },
        { MarkupSampleKind.Long, Read("long-message.txt") },
    };
    public static string Get(MarkupSampleKind kind)
        => Texts[kind];

    // Private methods

    private static string Read(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' is not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
