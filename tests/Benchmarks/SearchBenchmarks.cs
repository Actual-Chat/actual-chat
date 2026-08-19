using ActualChat.Search;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace ActualChat.Benchmarks;

/// <summary>
/// In-memory search benchmarks: <see cref="Search"/> builds a two-prefix query and matches +
/// ranks 1000 documents of 2-5 words (~20% match); <see cref="ExtractMatches"/> measures
/// <see cref="SearchQuery.GetMatchParts"/> over a 5-word text with 3 matched words.
/// Run: dotnet run -c Release --project tests/Benchmarks -- SearchBenchmarks
/// </summary>
[Config(typeof(InProcessShortRunConfig))]
[MemoryDiagnoser]
public class SearchBenchmarks
{
    private const int DocumentCount = 1000;
    private const string SearchText = "al sm"; // two prefixes

    private static readonly string[] Fillers =
        ["river", "bank", "table", "window", "garden", "planet", "music", "coffee", "puzzle", "forest"];
    private static readonly string[] PrefixAWords = ["Alice", "Alex", "Albert", "Alpha", "Almond"];
    private static readonly string[] PrefixBWords = ["Smith", "Small", "Smart", "Smile", "Smoke"];

    private SearchDocument[] _documents = [];
    private SearchQuery _highlightQuery;
    private string _highlightText = "";

    [GlobalSetup]
    public void Setup()
    {
        // 1000 documents of 2-5 words; every 5th carries a word per query prefix => ~20% match SearchText.
        var random = new Random(12345);
        _documents = new SearchDocument[DocumentCount];
        for (var i = 0; i < DocumentCount; i++) {
            var words = new string[random.Next(2, 6)];
            for (var j = 0; j < words.Length; j++)
                words[j] = Fillers[random.Next(Fillers.Length)];
            if (i % 5 == 0) {
                words[0] = PrefixAWords[random.Next(PrefixAWords.Length)];
                words[^1] = PrefixBWords[random.Next(PrefixBWords.Length)];
            }
            _documents[i] = new SearchDocument(string.Join(' ', words));
        }

        // Highlight scenario: a 5-word text; the query matches a prefix of 3 of the words.
        _highlightText = "Alice Bob Charlie David Emma";
        _highlightQuery = new SearchQuery("al ch em");
    }

    [Benchmark]
    public double Search()
    {
        // One search-as-you-type pass: build the query, then match + rank every document.
        var query = new SearchQuery(SearchText);
        var score = 0d;
        foreach (var document in _documents) {
            if (document.IsMatch(query))
                score += document.GetCoverageScore(query);
        }
        return score;
    }

    [Benchmark]
    public SearchMatchPart[] ExtractMatches()
        => _highlightQuery.GetMatchParts(_highlightText);
}
