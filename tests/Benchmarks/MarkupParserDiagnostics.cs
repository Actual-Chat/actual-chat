using ActualChat.Chat;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace ActualChat.Benchmarks;

// Run: dotnet run -c Release --project tests/Benchmarks -- MarkupParserDiagnostics

/// <summary>
/// Splits <see cref="MarkupParser"/>'s cost into per-call, per-element and per-char parts, which is
/// what tells you where to look: <see cref="MarkupParserBenchmarks"/> only says the total moved.
/// A 500-char single word costing a fraction of 100 short words is the shape to expect - the
/// grammar's price is paid per inline element, not per character.
/// </summary>
[Config(typeof(InProcessShortRunConfig))]
[MemoryDiagnoser]
public class MarkupParserDiagnostics
{
    private static readonly string Tiny = "a";
    private static readonly string Words100 = string.Join(' ', Enumerable.Repeat("word", 100));
    private static readonly string OneLongWord = new('a', 500);
    private static readonly string Lines100 = string.Join('\n', Enumerable.Repeat("word word word word", 100));
    private static readonly string Emails100 = string.Join(' ', Enumerable.Repeat("a@b.com", 100));
    private readonly MarkupParser _parser = new();

    [Benchmark]
    public Markup ParseTiny()
        => _parser.Parse(Tiny);

    [Benchmark]
    public Markup ParseWords100()
        => _parser.Parse(Words100);

    [Benchmark]
    public Markup ParseOneLongWord()
        => _parser.Parse(OneLongWord);

    [Benchmark]
    public Markup ParseLines100()
        => _parser.Parse(Lines100);

    [Benchmark]
    public Markup ParseEmails100()
        => _parser.Parse(Emails100);
}
