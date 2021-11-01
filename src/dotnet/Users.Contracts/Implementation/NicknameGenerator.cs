namespace ActualChat.Users;

public class NicknameGenerator : INicknameGenerator
{
    private static readonly string[] _nicknamePartOne = new[]{
        "Professor",
        "Doctor",
        "Dinosaur",
        "Milkman",
        "Plumber",
    };

    // just some random elf names
    private static readonly string[] _nicknamePartTwo = new[]{
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

    public ValueTask<string> Generate(CancellationToken cancellationToken = default)
    {
        var partOne = _nicknamePartOne[Random.Shared.Next(0, _nicknamePartOne.Length)];
        var partTwo = _nicknamePartTwo[Random.Shared.Next(0, _nicknamePartTwo.Length)];
        return new(partOne + partTwo + Random.Shared.Next(0, 100).ToString(CultureInfo.InvariantCulture));
    }
}
