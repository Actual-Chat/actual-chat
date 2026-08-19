using ActualChat.Chat;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace ActualChat.Benchmarks;

/// <summary>
/// <see cref="MarkupParser"/> throughput on real Voxt messages - see <see cref="MarkupSamples"/>.
/// The "Chars/s" column is the number the parser is judged by; a second parser implementation
/// should show up here as another [Benchmark] method rather than another run. One operation
/// parses every part of a sample - only <see cref="MarkupSampleKind.PlainShort"/> has several.
/// Run: dotnet run -c Release --project tests/Benchmarks -- MarkupParserBenchmarks
/// </summary>
[Config(typeof(Config))]
[MemoryDiagnoser]
public class MarkupParserBenchmarks
{
    private string[] _texts = [];
    private MarkupParser _parser = null!;

    [Params(
        MarkupSampleKind.PlainShort,
        MarkupSampleKind.PlainEnglish,
        MarkupSampleKind.PlainRussian,
        MarkupSampleKind.Regular,
        MarkupSampleKind.Table,
        MarkupSampleKind.Long,
        MarkupSampleKind.VideoStats,
        MarkupSampleKind.VideoStatsFenced,
        MarkupSampleKind.ConsoleLog,
        MarkupSampleKind.ConsoleLogFenced)]
    public MarkupSampleKind Sample { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _texts = MarkupSamples.Get(Sample);
        _parser = new MarkupParser();
    }

    [Benchmark(Baseline = true)]
    public Markup Parse()
    {
        // What the app actually calls: parse + simplify.
        var result = MarkupParser.EmptyResult;
        foreach (var text in _texts)
            result = _parser.Parse(text);
        return result;
    }

    [Benchmark]
    public Markup ParseRawOnly()
    {
        var result = MarkupParser.EmptyResult;
        foreach (var text in _texts)
            result = MarkupParser.ParseRaw(text);
        return result;
    }

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

            var charsPerSecond = MarkupSamples.GetLength(sample) / (mean / 1e9);
            return charsPerSecond.ToString("N0");
        }
    }
}
