using ActualChat.Chat;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace ActualChat.Benchmarks;

// Run: dotnet run -c Release --project tests/Benchmarks -- MarkupParserBenchmarks
// The equivalence check alone: dotnet run -c Release --project tests/Benchmarks -- verify
// Setup fails the run when the parsers disagree on the sample, so a timing is never reported
// for a parser that isn't equivalent.

/// <summary>
/// Markup parser throughput on three real Voxt messages (<see cref="MarkupSamples"/>): the shipping
/// Pidgin-based <see cref="MarkupParser"/> against the ParsecSharp port
/// <see cref="ParsecMarkupParser"/>. "Chars/s" is the column they're judged by.
/// </summary>
[Config(typeof(Config))]
[MemoryDiagnoser]
public class MarkupParserBenchmarks
{
    private string _text = "";
    private MarkupParser _pidgin = null!;
    private ParsecMarkupParser _parsec = null!;
    [Params(MarkupSampleKind.Regular, MarkupSampleKind.Table, MarkupSampleKind.Long)]
    public MarkupSampleKind Sample { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _text = MarkupSamples.Get(Sample);
        _pidgin = new MarkupParser();
        _parsec = new ParsecMarkupParser();
        var mismatches = MarkupParserEquivalence.Verify([_text]);
        if (mismatches.Count != 0)
            throw new InvalidOperationException(
                "Parser implementations disagree on this sample:" + Environment.NewLine
                + string.Join(Environment.NewLine, mismatches));
    }

    [Benchmark(Baseline = true)]
    public Markup Pidgin()
        => _pidgin.Parse(_text);

    [Benchmark]
    public Markup Parsec()
        => _parsec.Parse(_text);

    [Benchmark]
    public Markup PidginRaw()
        => MarkupParser.ParseRaw(_text);

    [Benchmark]
    public Markup ParsecRaw()
        => ParsecMarkupParser.ParseRaw(_text);

    // Nested types

    private sealed class Config : ManualConfig
    {
        public Config()
        {
            AddJob(BenchmarkJobs.ShortRunInProcess);
            AddColumn(new CharsPerSecondColumn());
        }
    }

    private sealed class CharsPerSecondColumn : IColumn
    {
        public string Id => nameof(CharsPerSecondColumn);
        public string ColumnName => "Chars/s";
        public bool AlwaysShow => true;
        public ColumnCategory Category => ColumnCategory.Custom;
        public int PriorityInCategory => 0;
        public bool IsNumeric => true;
        public UnitType UnitType => UnitType.Dimensionless;
        public string Legend => "Input characters parsed per second";
        public bool IsAvailable(Summary summary) => true;
        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
            => GetValue(summary, benchmarkCase);

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
            var mean = summary[benchmarkCase]?.ResultStatistics?.Mean ?? 0;
            if (mean <= 0)
                return "?";

            var kind = benchmarkCase.Parameters.Items
                .Where(x => x.Name == nameof(Sample))
                .Select(x => (MarkupSampleKind?)x.Value)
                .FirstOrDefault();
            if (kind is not { } sample)
                return "?";

            var charsPerSecond = MarkupSamples.Get(sample).Length / (mean / 1e9);
            return charsPerSecond.ToString("N0");
        }
    }
}
