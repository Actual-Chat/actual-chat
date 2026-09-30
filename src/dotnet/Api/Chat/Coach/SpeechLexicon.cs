using System.Text.Json;
using System.Text.RegularExpressions;

namespace ActualChat.Chat;

/// <summary>
/// Words that can be marked without asking the LLM, per language: every entry of SpeechLexicon.json is a
/// pattern that must match a whole word, or a stretch of text for scripts without word spaces. Shared with
/// the client, which marks a live transcript with it while the words are still arriving.
/// </summary>
public sealed class SpeechLexicon
{
    private const string ResourceName = "SpeechLexicon.json";
    private const string WholeWordProperty = "wholeWord";
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex TokenRegex = new(
        @"[\p{L}\p{M}\p{Nd}]+(?:[-'’][\p{L}\p{M}\p{Nd}]+)*",
        RegexOptions.CultureInvariant,
        MatchTimeout);

    private readonly Dictionary<string, LanguageLexicon> _languages;

    public static SpeechLexicon Default => field ??= Load();

    public IReadOnlyCollection<string> Languages => _languages.Keys;

    private SpeechLexicon(Dictionary<string, LanguageLexicon> languages)
        => _languages = languages;

    public int Count(string iso, SpeechSpanKind kind)
        => _languages.TryGetValue(iso, out var lexicon) && lexicon.Patterns.TryGetValue(kind, out var patterns)
            ? patterns.Count
            : 0;

    public bool IsWholeWord(Language language)
        => !_languages.TryGetValue(language.IsoCode, out var lexicon) || lexicon.IsWholeWord;

    // startIndex must be a word boundary for a whole-word language
    public ApiArray<SpeechSpan> FindSpans(string text, Language? language, int startIndex = 0)
    {
        if (language is null || text.IsNullOrEmpty() || startIndex >= text.Length)
            return ApiArray<SpeechSpan>.Empty;
        if (!_languages.TryGetValue(language.IsoCode, out var lexicon))
            return ApiArray<SpeechSpan>.Empty;

        var spans = new List<SpeechSpan>();
        if (!lexicon.IsWholeWord) {
            foreach (var (kind, patterns) in lexicon.Patterns)
                for (var match = patterns.Regex.Value.Match(text, startIndex); match.Success; match = match.NextMatch())
                    spans.Add(NewSpan(kind, match));
            return spans.OrderBy(s => s.Start).ToApiArray();
        }

        for (var token = TokenRegex.Match(text, startIndex); token.Success; token = token.NextMatch())
            foreach (var (kind, patterns) in lexicon.Patterns) {
                if (!patterns.Regex.Value.IsMatch(token.ValueSpan))
                    continue;

                spans.Add(NewSpan(kind, token));
                break;
            }
        return spans.ToApiArray();
    }

    // Private methods

    private static SpeechSpan NewSpan(SpeechSpanKind kind, Match match)
        => new (kind, match.Value.ToLower(), match.Index, match.Length, ApiArray<string>.Empty);

    private static SpeechLexicon Load()
    {
        using var stream = typeof(SpeechLexicon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Resource '{ResourceName}' is missing.");
        using var doc = JsonDocument.Parse(stream);
        var languages = new Dictionary<string, LanguageLexicon>();
        foreach (var language in doc.RootElement.EnumerateObject()) {
            var isWholeWord = true;
            var lists = new Dictionary<SpeechSpanKind, List<string>>();
            foreach (var property in language.Value.EnumerateObject()) {
                if (property.Name == WholeWordProperty) {
                    isWholeWord = property.Value.GetBoolean();
                    continue;
                }
                var kind = Enum.Parse<SpeechSpanKind>(property.Name, ignoreCase: true);
                lists[kind] = property.Value.EnumerateArray().Select(x => x.GetString()!).ToList();
            }
            var patterns = lists.ToDictionary(
                x => x.Key,
                x => new PatternGroup(x.Value.Count, new Lazy<Regex>(() => NewRegex(x.Value, isWholeWord))));
            languages[language.Name] = new LanguageLexicon(isWholeWord, patterns);
        }
        return new SpeechLexicon(languages);
    }

    private static Regex NewRegex(List<string> patterns, bool isWholeWord)
    {
        var alternatives = string.Join("|", patterns.Select(p => $"(?:{p})"));
        var pattern = isWholeWord ? $"^(?:{alternatives})$" : $"(?:{alternatives})";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
    }

    // Nested types

    // The regex is built when its language is first asked for: a client loads the lexicon at startup
    private sealed record PatternGroup(int Count, Lazy<Regex> Regex);

    private sealed record LanguageLexicon(bool IsWholeWord, Dictionary<SpeechSpanKind, PatternGroup> Patterns);
}
