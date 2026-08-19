using ActualChat.Chat;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace ActualChat.Benchmarks;

/// <summary>
/// <see cref="MarkupParser"/> throughput on two real Voxt messages - see <see cref="MarkupSamples"/>.
/// The "Chars/s" column is the number the parser is judged by; a second parser implementation
/// should show up here as another [Benchmark] method rather than another run.
/// Run: dotnet run -c Release --project tests/Benchmarks -- MarkupParserBenchmarks
/// </summary>
[Config(typeof(Config))]
[MemoryDiagnoser]
public class MarkupParserBenchmarks
{
    private string _text = "";
    private MarkupParser _parser = null!;
    [Params(MarkupSampleKind.Regular, MarkupSampleKind.Table, MarkupSampleKind.Long)]
    public MarkupSampleKind Sample { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _text = MarkupSamples.Get(Sample);
        _parser = new MarkupParser();
    }

    [Benchmark(Baseline = true)]
    public Markup Parse()
        // What the app actually calls: parse + simplify.
        => _parser.Parse(_text);

    [Benchmark]
    public Markup ParseRawOnly()
        => MarkupParser.ParseRaw(_text);

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
