namespace ActualChat.Roulette;

public static class Interests
{
    private static readonly Dictionary<string, string> InterestTitles = new ();
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

// public class InterestsCollection : ICollection<Interest>
// {
//     private readonly ImmutableArray<Interest> _interests = new();
//
//     public IEnumerator<Interest> GetEnumerator()
//         => ((IEnumerable<Interest>)_interests).GetEnumerator();
//
//     IEnumerator IEnumerable.GetEnumerator()
//         => GetEnumerator();
//
//     public void Add(Interest item)
//         => throw new NotImplementedException();
//
//     public void Clear()
//         => throw new NotImplementedException();
//
//     public bool Contains(Interest item)
//         => throw new NotImplementedException();
//
//     public void CopyTo(Interest[] array, int arrayIndex)
//         => _interests.CopyTo(array, arrayIndex);
//
//     public bool Remove(Interest item)
//         => throw new NotImplementedException();
//
//     public int Count { get; }
//         =>
//     public bool IsReadOnly { get; }
// }
