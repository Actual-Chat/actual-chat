using System.Text.Json;
using System.Text.RegularExpressions;

namespace ActualChat.Chat.ML;

/// <summary>
/// Words that can be marked without asking the LLM, per language: every entry of SpeechLexicon.json is a
/// pattern that must match a whole word, or a stretch of text for scripts without word spaces.
/// </summary>
public sealed class SpeechLexicon
{
    private const string ResourceName = "SpeechLexicon.json";
    private const string WholeWordProperty = "wholeWord";
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex TokenRegex = new(
        @"[\p{L}\p{M}\p{Nd}]+(?:[-'’][\p{L}\p{M}\p{Nd}]+)*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        MatchTimeout);

    private readonly Dictionary<string, LanguageLexicon> _languages;

    public static SpeechLexicon Default => field ??= Load();

    public IReadOnlyCollection<string> Languages => _languages.Keys;

    private SpeechLexicon(Dictionary<string, LanguageLexicon> languages)
        => _languages = languages;

    public int Count(string iso, SpeechSpanKind kind)
        => _languages.TryGetValue(iso, out var lexicon) && lexicon.Patterns.TryGetValue(kind, out var group)
            ? group.Count
            : 0;

    public ApiArray<SpeechSpan> FindSpans(string text, Language? language)
    {
        if (language is null || text.IsNullOrEmpty())
            return ApiArray<SpeechSpan>.Empty;

        if (!_languages.TryGetValue(language.IsoCode, out var lexicon))
            return ApiArray<SpeechSpan>.Empty;

        var spans = new List<SpeechSpan>();
        foreach (var (kind, group) in lexicon.Patterns) {
            if (lexicon.IsWholeWord)
                foreach (var token in TokenRegex.Matches(text).Cast<Match>().Where(m => group.Regex.IsMatch(m.Value)))
                    spans.Add(NewSpan(kind, token));
            else
                foreach (var match in group.Regex.Matches(text).Cast<Match>())
                    spans.Add(NewSpan(kind, match));
        }
        return spans.OrderBy(s => s.Start).ToApiArray();
    }

    // Private methods

    private static SpeechSpan NewSpan(SpeechSpanKind kind, Match match)
        => new (kind, match.Value.ToLowerInvariant(), match.Index, match.Length, ApiArray<string>.Empty);

    private static SpeechLexicon Load()
    {
        using var stream = typeof(SpeechLexicon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Resource '{ResourceName}' is missing.");
        using var doc = JsonDocument.Parse(stream);
        var languages = new Dictionary<string, LanguageLexicon>(StringComparer.Ordinal);
        foreach (var language in doc.RootElement.EnumerateObject()) {
            var isWholeWord = true;
            var patterns = new Dictionary<SpeechSpanKind, PatternGroup>();
            foreach (var property in language.Value.EnumerateObject()) {
                if (property.Name == WholeWordProperty) {
                    isWholeWord = property.Value.GetBoolean();
                    continue;
                }
                var kind = Enum.Parse<SpeechSpanKind>(property.Name, ignoreCase: true);
                var list = property.Value.EnumerateArray().Select(x => x.GetString()!).ToList();
                patterns[kind] = new PatternGroup(list.Count, NewRegex(list, isWholeWord));
            }
            languages[language.Name] = new LanguageLexicon(isWholeWord, patterns);
        }
        return new SpeechLexicon(languages);
    }

    private static Regex NewRegex(List<string> patterns, bool isWholeWord)
    {
        var alternatives = string.Join("|", patterns.Select(p => $"(?:{p})"));
        var pattern = isWholeWord ? $"^(?:{alternatives})$" : $"(?:{alternatives})";
        return new Regex(
            pattern,
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            MatchTimeout);
    }

    // Nested types

    private sealed record PatternGroup(int Count, Regex Regex);

    private sealed record LanguageLexicon(bool IsWholeWord, Dictionary<SpeechSpanKind, PatternGroup> Patterns);
}
