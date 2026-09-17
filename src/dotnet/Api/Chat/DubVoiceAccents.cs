namespace ActualChat.Chat;

/// <summary>
/// Maps a spoken language to the <see cref="DubVoice.Accent"/> a speaker of it most likely wants,
/// picks the voices to suggest for a speaker's languages, and resolves the voice a speaker is dubbed
/// with: the chosen one, or - when none is chosen - the first suggestion.
/// </summary>
public static class DubVoiceAccents
{
    public const string Default = "american";
    public const int MaxSuggested = 8;
    public const int MaxSuggestedPerGender = 4;
    public const int MinSuggested = 4;

    private const string Conversational = "conversational";

    private static readonly Dictionary<string, string> ByTag = new() {
        { "es-MX", "latin_american" },
        { "es-US", "latin_american" },
        { "pt-BR", "brazilian" },
        { "en-IN", "indian" },
        { "en-GB", "british" },
    };

    private static readonly Dictionary<string, string> ByIsoCode = new() {
        { "ru", "slavic" },
        { "uk", "slavic" },
        { "pl", "slavic" },
        { "cs", "slavic" },
        { "bg", "slavic" },
        { "hr", "slavic" },
        { "sr", "slavic" },
        { "bs", "slavic" },
        { "cnr", "slavic" },
        { "es", "spanish" },
        { "pt", "portuguese" },
        { "hi", "indian" },
        { "mr", "indian" },
        { "pa", "indian" },
        { "ta", "indian" },
        { "ur", "indian" },
        { "en", "american" },
        { "ja", "japanese" },
        { "ko", "korean" },
        { "zh", "chinese" },
        { "fr", "french" },
        { "de", "german" },
        { "it", "italian" },
        { "id", "southeast_asian" },
        { "ms", "southeast_asian" },
        { "th", "southeast_asian" },
        { "vi", "southeast_asian" },
        { "tl", "southeast_asian" },
        { "fil", "southeast_asian" },
    };

    public static string ForLanguage(Language language)
        => ByTag.GetValueOrDefault(language.Value)
            ?? ByIsoCode.GetValueOrDefault(language.IsoCode)
            ?? Default;

    // The voice a speaker is dubbed with: their choice when the catalog lists it, otherwise the first
    // suggestion for their languages; null only when the catalog is empty (the synthesizer's default)
    public static string? ResolveVoice(
        string chosenVoiceId,
        IReadOnlyList<DubVoice> voices,
        IEnumerable<Language> languages)
    {
        if (!chosenVoiceId.IsNullOrEmpty() && voices.Any(v => v.Id == chosenVoiceId))
            return chosenVoiceId;

        return Suggest(voices, languages).FirstOrDefault()?.Id;
    }

    // Per language in order: its accent's voices, conversational ones first, genders interleaved,
    // up to MaxSuggestedPerGender of each and MaxSuggested overall; padded with Default-accent voices
    // when the languages alone yield fewer than MinSuggested. Catalog order within a group, no repeats.
    public static List<DubVoice> Suggest(IReadOnlyList<DubVoice> voices, IEnumerable<Language> languages)
    {
        var result = new List<DubVoice>();
        var seen = new HashSet<string>();
        var maleCount = 0;
        var femaleCount = 0;
        foreach (var accent in languages.Select(ForLanguage).Distinct())
            AddAccent(accent);
        if (result.Count < MinSuggested)
            AddAccent(Default);
        return result;

        void AddAccent(string accent) {
            var group = voices
                .Where(v => v.Accent == accent && !seen.Contains(v.Id))
                .OrderBy(v => v.UseCase.Contains(Conversational) ? 0 : 1)
                .ToList();
            var males = new Queue<DubVoice>(group.Where(v => v.Gender == "male"));
            var females = new Queue<DubVoice>(group.Where(v => v.Gender == "female"));
            while (result.Count < MaxSuggested) {
                var canAddMale = males.Count > 0 && maleCount < MaxSuggestedPerGender;
                var canAddFemale = females.Count > 0 && femaleCount < MaxSuggestedPerGender;
                if (!canAddMale && !canAddFemale)
                    break;

                if (canAddMale) {
                    Add(males.Dequeue());
                    maleCount++;
                }
                if (canAddFemale && result.Count < MaxSuggested) {
                    Add(females.Dequeue());
                    femaleCount++;
                }
            }
        }

        void Add(DubVoice voice) {
            seen.Add(voice.Id);
            result.Add(voice);
        }
    }
}
