using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserLanguageSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserLanguageSettings);

    [DataMember, MemoryPackOrder(0)] public Language Primary { get; init; } = Languages.Main;
    [DataMember, MemoryPackOrder(1)] public Language? Secondary { get; init; }
    [DataMember, MemoryPackOrder(3)] public Language? Tertiary { get; init; }
    [DataMember, MemoryPackOrder(2)] public string Origin { get; init; } = "";

    public List<Language> ToList()
    {
        var result = new List<Language>();
        if (!Primary.IsNone)
            result.Add(Primary);
        if (Secondary is { IsNone: false } secondary && !result.Contains(secondary))
            result.Add(secondary);
        if (Tertiary is { IsNone: false } tertiary && !result.Contains(tertiary))
            result.Add(tertiary);
        if (result.Count == 0)
            result.Add(Languages.Main);
        return result;
    }

    public UserLanguageSettings With(int index, Language language)
    {
        if (index is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(index));

        var languages = ToList();
        if (index < languages.Count)
            languages[index] = language;
        else
            languages.Add(language);
        languages = languages.Where(x => !x.IsNone).DistinctBy(x => x.Id).ToList();

        return this with {
            Primary = languages.GetOrDefault(0, Languages.Main),
            Secondary = languages.GetOrDefault(1).NullIfNone(),
            Tertiary = languages.GetOrDefault(2).NullIfNone(),
        };
    }
}
