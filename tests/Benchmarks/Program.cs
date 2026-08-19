using ActualChat.Benchmarks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

var assembly = typeof(Program).Assembly;

// `dotnet run -- verify` checks the alternative parser implementations against the current one
// without running a single benchmark - the answer you want before reading any timing.
if (args is ["verify", ..]) {
    var mismatches = MarkupParserEquivalence.Verify();
    foreach (var mismatch in mismatches)
        Console.WriteLine(mismatch);
    Console.WriteLine(mismatches.Count == 0
        ? $"ParsecMarkupParser matches MarkupParser on all {MarkupParserEquivalence.Corpus.Length} corpus inputs."
        : $"{mismatches.Count} mismatch(es).");
    return mismatches.Count == 0 ? 0 : 1;
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
