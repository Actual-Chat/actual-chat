namespace ActualChat;

public static class Interests
{
    public static readonly Interest Flexible = new ("flexible", "Flexible");
    public static readonly Interest Food = new ("food", "Food");
    public static readonly Interest CinemaAndTvShows = new ("cinema-and-tv-shows", "Cinema & TV Shows");
    public static readonly Interest Education = new ("education", "Education");
    public static readonly Interest Jokes = new ("jokes", "Jokes");
    public static readonly Interest Traveling = new ("traveling", "Traveling");
    public static readonly Interest Culture = new ("culture", "Culture");
    public static readonly Interest SportAndFitness = new ("sport-and-fitness", "Sport & Fitness");
    public static readonly Interest Gaming = new ("gaming", "Gaming");
    public static readonly Interest LifeAdvice = new ("life-advice", "Life Advice");
    public static readonly Interest LanguageSwap = new ("language-swap", "Language Swap");

    public static readonly Interest[] All = [
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

    public static readonly Dictionary<string, Interest> ById
        = All.ToDictionary(x => x.Id.Value, StringComparer.Ordinal);
}
