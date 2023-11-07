using Cysharp.Text;
using Microsoft.Toolkit.HighPerformance;
using Stl.Mathematics.Internal;

namespace ActualChat;

public sealed record RandomNameGenerator
{
    private static readonly string[] Prefixes = {
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

    private static readonly string[] Suffixes = {
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

    public static readonly RandomNameGenerator Default = new();

    public char WordDelimiter { get; init; } = ' ';

    public string Generate() => Generate(Random.Shared.Next());
    public string Generate(string seed) => Generate(seed.GetDjb2HashCode());
    public string Generate(int seed)
    {
        var prefixIndex = IntArithmetics.Default.Mod(seed, Prefixes.Length);
        var suffixIndex = IntArithmetics.Default.Mod(seed * 1019, Suffixes.Length);
        var name = ZString.Concat(
            Prefixes[prefixIndex], WordDelimiter,
            Suffixes[suffixIndex], WordDelimiter);
        return name;
    }
}
