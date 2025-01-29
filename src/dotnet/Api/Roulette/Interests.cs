namespace ActualChat.Roulette;

public static class Interests
{
    private static readonly Dictionary<string, string> InterestTitles = new (StringComparer.Ordinal);
    public static readonly ApiArray<Interest> All;

    public static readonly Interest Flexible = new ("flexible");
    public static readonly Interest Food = new ("food");
    public static readonly Interest CinemaAndTvShows = new ("cinema-and-tv-shows");
    public static readonly Interest Education = new ("education");
    public static readonly Interest Jokes = new ("jokes");
    public static readonly Interest Traveling = new ("traveling");
    public static readonly Interest Culture = new ("culture");
    public static readonly Interest SportAndFitness = new ("sport-and-fitness");
    public static readonly Interest Gaming = new ("gaming");
    public static readonly Interest LifeAdvice = new ("life-advice");
    public static readonly Interest LanguageSwap = new ("language-swap");

    static Interests()
    {
        All = [
            Flexible,
            Food,
            CinemaAndTvShows,
            Education,
            Jokes,
            Traveling,
            Culture,
            SportAndFitness,
            Gaming,
            LifeAdvice,
            LanguageSwap
        ];

        RegisterInterestTitle(Flexible, "Flexible");
        RegisterInterestTitle(Food, "Food");
        RegisterInterestTitle(CinemaAndTvShows, "Cinema & TV Shows");
        RegisterInterestTitle(Education, "Education");
        RegisterInterestTitle(Jokes, "Jokes");
        RegisterInterestTitle(Traveling, "Traveling");
        RegisterInterestTitle(Culture, "Culture");
        RegisterInterestTitle(SportAndFitness, "Sport & fitness");
        RegisterInterestTitle(Gaming, "Gaming");
        RegisterInterestTitle(LifeAdvice, "Life Advice");
        RegisterInterestTitle(LanguageSwap, "Language Swap");
    }

    public static string GetTitle(Interest interest)
        => InterestTitles.TryGetValue(interest.Code, out var title) ? title : interest.Code;

    private static void RegisterInterestTitle(Interest interest, string title)
        => InterestTitles.Add(interest.Code, title);
}
