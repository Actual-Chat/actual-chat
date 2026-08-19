using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

var assembly = typeof(Program).Assembly;

// Translate prompt-style arguments (number, class name, "*") into BDN --filter args
// so you can run e.g. `dotnet run -- 0` or `dotnet run -- AsyncMemoizerBenchmarks`
// instead of pressing through the interactive prompt.
// Tokens starting with `--` are passed through to BDN unchanged.
if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)) {
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
