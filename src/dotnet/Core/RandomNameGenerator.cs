using Cysharp.Text;
using Microsoft.Toolkit.HighPerformance;
using Stl.Mathematics.Internal;

namespace ActualChat;

public sealed record RandomNameGenerator
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

    public static readonly RandomNameGenerator Default = new();

    public char WordDelimiter { get; init; } = ' ';

    public string Generate() => Generate(Random.Shared.Next());
    public string Generate(string seed) => Generate(seed.GetDjb2HashCode());
    public string Generate(int seed)
    {
        var prefixIndex = IntArithmetics.Default.Mod(seed, Prefixes.Length);
        var suffixIndex = IntArithmetics.Default.Mod(seed * 1019, Suffixes.Length);
        var extraIndex = IntArithmetics.Default.Mod(seed * 353, 10);
        var name = ZString.Concat(
            Prefixes[prefixIndex], WordDelimiter,
            Suffixes[suffixIndex], WordDelimiter,
            extraIndex.ToString(CultureInfo.InvariantCulture));
        return name;
    }
}
