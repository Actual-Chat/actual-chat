using System.Diagnostics;
using ActualChat.Benchmarks;
using ActualChat.Chat;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

var assembly = typeof(Program).Assembly;

// `profile <sample> <seconds>` parses one sample in a loop, so an external sampling profiler
// (dotnet-trace collect --format speedscope -- <this exe> profile Long 20) has a steady workload
// to sample. BenchmarkDotNet's own harness is not usable here: its iterations are short and
// interleaved with its own bookkeeping, which lands in the profile too.
if (args is ["profile", ..]) {
    var sample = args.Length > 1 ? Enum.Parse<MarkupSampleKind>(args[1], true) : MarkupSampleKind.Long;
    var duration = TimeSpan.FromSeconds(args.Length > 2 ? double.Parse(args[2]) : 20);
    var texts = MarkupSamples.Get(sample);
    var length = MarkupSamples.GetLength(sample);
    var parser = new MarkupParser();
    Console.WriteLine($"Parsing {sample} ({length} chars) for {duration.TotalSeconds}s...");
    var count = 0L;
    var stopwatch = Stopwatch.StartNew();
    while (stopwatch.Elapsed < duration) {
        for (var i = 0; i < 16; i++) // The clock check itself is not free, so amortize it
            foreach (var text in texts)
                _ = parser.Parse(text);
        count += 16;
    }
    stopwatch.Stop();
    var charsPerSecond = count * length / stopwatch.Elapsed.TotalSeconds;
    Console.WriteLine($"{count} rounds in {stopwatch.Elapsed.TotalSeconds:N1}s, {charsPerSecond:N0} chars/s.");
    return 0;
}

// Translate prompt-style arguments (number, class name, "*") into BDN --filter args
// so you can run e.g. `dotnet run -- 0` or `dotnet run -- AsyncMemoizerBenchmarks`
// instead of pressing through the interactive prompt.
// Tokens starting with `--` are passed through to BDN unchanged.
if (args.Length > 0 && !args[0].StartsWith("--")) {
    var benchmarkTypes = assembly.GetTypes()
        .Where(t => !t.IsAbstract && t.GetMethods()
            .Any(m => m.GetCustomAttribute<BenchmarkAttribute>() != null))
        .OrderBy(t => t.Name)
        .ToArray();

    var filters = new List<string>();
    foreach (var token in args) {
        if (token == "*") {
            filters.Add("*");
        }
        else if (int.TryParse(token, out var idx) && idx >= 0 && idx < benchmarkTypes.Length) {
            filters.Add($"*{benchmarkTypes[idx].Name}*");
        }
        else {
            filters.Add($"*{token}*");
        }
    }
    args = ["--filter", .. filters];
}

BenchmarkSwitcher.FromAssembly(assembly).Run(args);
return 0;
