namespace ActualChat;

/// <summary>
/// Generates random display names by combining prefixes and suffixes.
/// </summary>
public sealed record RandomNameGenerator
{
    private static readonly string[] DefaultPrefixes = {
        "Dr.",
        "Professor",
        "Smiling",
        "Amazing",
        "Bright",
        "Cute",
        "Dancing",
        "Enchanted",
        "Funny",
        "Happy",
        "Innocent",
        "Joyful",
        "Kind",
        "Lovely",
        "Marvelous",
        "Mega",
        "Naval",
        "Relaxing",
        "Smiling",
        "Terrific",
        "Uber",
        "Ultimate",
        "Vicious",
        "Wandering",
    };

    private static readonly string[] DefaultSuffixes = {
        // Non-existing
        "Bithana",
        "Ermys",
        "Helehana",
        "Glyneor",
        "Helenelis",
        "Nerp",
        "Quixote",
        "Vaeril",
        "Virnala",

        // Fictional + real
        "Akira",
        "Athos",
        "Aramis",
        "Fiona",
        "Frodo",
        "Camille",
        "Everdene",
        "Eva",
        "Dorian",
        "Fosco",
        "Horatio",
        "Ophelia",
        "Porthos",
        "Scarlett",
        "Shrek",
        "Starbuck",
        "Tarzan",
        "Tristan",
        "Viola",

        // Animals
        "Dolphin",
        "Gatto",
        "Puss",
        "Tuna",
        "Wolf",
        "Zerling",
    };

    private static readonly string[] PersonFirstNames = {
        "Aiko", "Amara", "Ana", "Anders", "Aria", "Bilal",
        "Camila", "Chen", "Daria", "Diego", "Elena", "Emeka",
        "Fatima", "Felix", "Grace", "Hana", "Hugo", "Ingrid",
        "Isabela", "Ivan", "Jae", "Jonas", "Julia", "Kai",
        "Karim", "Lars", "Layla", "Leo", "Lucia", "Mateo",
        "Maya", "Mei", "Miguel", "Nadia", "Niko", "Nora",
        "Omar", "Priya", "Rafael", "Rania", "Ravi", "Rosa",
        "Sofia", "Tariq", "Tomas", "Yara", "Yuki", "Zara",
    };

    private static readonly string[] PersonLastNames = {
        "Abadi", "Adeyemi", "Almeida", "Andersen", "Bauer", "Bianchi",
        "Chen", "Costa", "Dalgaard", "Diallo", "Dubois", "Eriksen",
        "Farrell", "Fischer", "Garcia", "Gupta", "Haddad", "Hansen",
        "Ibrahim", "Ivanov", "Jansen", "Kaur", "Kim", "Kovacs",
        "Larsen", "Lindqvist", "Lopez", "Mbeki", "Moreau", "Nakamura",
        "Novak", "Okafor", "Oliveira", "Petrov", "Popescu", "Reyes",
        "Rossi", "Sandberg", "Santos", "Sharma", "Silva", "Tanaka",
        "Tran", "Vargas", "Vogel", "Wang", "Yilmaz", "Zielinski",
    };

    public static readonly RandomNameGenerator Default = new();
    public static readonly RandomNameGenerator Person = new() {
        Prefixes = PersonFirstNames,
        Suffixes = PersonLastNames,
    };

    public string[] Prefixes { get; init; } = DefaultPrefixes;
    public string[] Suffixes { get; init; } = DefaultSuffixes;
    public string WordDelimiter { get; init; } = " ";

    public string Generate() => Generate(Random.Shared.Next());
    public string Generate(string seed) => Generate(seed.GetXxHash3());
    public string Generate(int seed)
    {
        var (prefix, suffix) = GenerateParts(seed);
        return string.Concat(prefix, WordDelimiter, suffix);
    }

    public (string Prefix, string Suffix) GenerateParts(string seed) => GenerateParts(seed.GetXxHash3());
    public (string Prefix, string Suffix) GenerateParts(int seed)
    {
        var prefixIndex = seed.PositiveModulo(Prefixes.Length);
        var suffixIndex = (seed * 1019).PositiveModulo(Suffixes.Length);
        return (Prefixes[prefixIndex], Suffixes[suffixIndex]);
    }
}
