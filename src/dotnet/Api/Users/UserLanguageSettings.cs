using ActualChat.Kvas;
using ActualChat.Serialization;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserLanguageSettings : IHasOrigin, IHasKvasKey<UserLanguageSettings>
{
    public static string KvasKey => nameof(UserLanguageSettings);

    [DataMember, MemoryPackOrder(0), LegacyLanguageFormatter(false)]
    [field: AllowNull, MaybeNull]
    public Language Primary {
        get => field ?? Languages.Main;
        init;
    }
    [DataMember, MemoryPackOrder(1), LegacyLanguageFormatter(true)]
    public Language? Secondary { get; init; }
    [DataMember, MemoryPackOrder(3), LegacyLanguageFormatter(true)]
    public Language? Tertiary { get; init; }
    [DataMember, MemoryPackOrder(2)]
    public string Origin { get; init; } = "";

    public List<Language> ListSpoken()
    {
        var result = new List<Language> { Primary };
        if (Secondary is { } secondary && !result.Contains(secondary))
            result.Add(secondary);
        if (Tertiary is { } tertiary && !result.Contains(tertiary))
            result.Add(tertiary);
        if (result.Count == 0)
            result.Add(Languages.Main);
        return result;
    }

    public UserLanguageSettings With(int index, Language? language)
    {
        if (index is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(index));

        var languages = ListSpoken();
        if (index < languages.Count)
            languages[index] = language!;
        else if (language != null)
            languages.Add(language);
        languages = languages.SkipNullItems().DistinctBy(x => x.Id).ToList();

        // ReSharper disable once WithExpressionModifiesAllMembers
        return this with {
            Primary = languages.GetOrDefault(0, Languages.Main),
            Secondary = languages.GetOrDefault(1),
            Tertiary = languages.GetOrDefault(2),
        };
    }
}
