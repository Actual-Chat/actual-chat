using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserLanguageSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserLanguageSettings);

    [DataMember, MemoryPackOrder(0)] [LanguageBackwardCompatibleFormatter(false)]
    [field: AllowNull, MaybeNull]
    public Language Primary {
        get => field ?? Languages.Main;
        init;
    }
    [DataMember, MemoryPackOrder(1)] [LanguageBackwardCompatibleFormatter(true)] public Language? Secondary { get; init; }
    [DataMember, MemoryPackOrder(3)] [LanguageBackwardCompatibleFormatter(true)] public Language? Tertiary { get; init; }
    [DataMember, MemoryPackOrder(2)] public string Origin { get; init; } = "";

    [IgnoreDataMember, MemoryPackIgnore]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<Language> AllSpoken {
        get {
            if (field != null)
                return field;

            var list = new List<Language> { Primary };
            if (Secondary is { } secondary && !list.Contains(secondary))
                list.Add(secondary);
            if (Tertiary is { } tertiary && !list.Contains(tertiary))
                list.Add(tertiary);
            if (list.Count == 0)
                list.Add(Languages.Main);
            return field = list.ToArray();
        }
    }

    public UserLanguageSettings With(int index, Language? language)
    {
        if (index is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(index));

        var languages = AllSpoken.ToList();
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
