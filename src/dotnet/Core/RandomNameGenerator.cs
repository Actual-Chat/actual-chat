
namespace ActualChat;

public interface IRandomNameGenerator
{
    /// <summary>
    /// Must generate a readable random name that's a sequence of words
    /// separated by <paramref name="wordDelimiter"/>.
    /// </summary>
    /// <returns>New name.</returns>
    string Generate(char wordDelimiter = ' ', bool lowerCase = false);
}

public class RandomNameGenerator : IRandomNameGenerator
{
    private static readonly string[] Prefixes = {
        "Professor",
        "Smiling",
        "Amazing",
        "Bright",
        "Black",
        "Cute",
        "Dancing",
        "Doctor",
        "Enchanted",
        "Funny",
        "Great",
        "Happy",
        "Innocent",
        "Joyful",
        "Kind",
        "King",
        "Lovely",
        "Marvelous",
        "Mega",
        "Naval",
        "Professor",
        "Quick",
        "Queen",
        "Relaxing",
        "Smiling",
        "Terrific",
        "Uber",
        "Ultimate",
        "Vicious",
        "Wandering",
        "Young",
    };

    // just some random elf names
    private static readonly string[] Suffixes = {
        "Zinsalor",
        "Ermys",
        "Vulmar",
        "Glynmenor",
        "Elauthin",
        "Helenelis",
        "Vaeril",
        "Bithana",
        "Bialaer",
        "Keypetor",
        "Ygannea",
        "Virnala",
        "Phaerille",
        "Helehana",
    };

    public string Generate(char wordDelimiter = ' ', bool lowerCase = false)
    {
        var prefix = Prefixes[Random.Shared.Next(0, Prefixes.Length)];
        var suffix = Suffixes[Random.Shared.Next(0, Suffixes.Length)];
        var name = prefix + wordDelimiter + suffix + wordDelimiter + Random.Shared.Next(0, 100).ToString(CultureInfo.InvariantCulture);
        if (lowerCase)
            name = name.ToLowerInvariant();
        return name;
    }
}
