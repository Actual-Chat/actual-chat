using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Roulette;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;

namespace ActualChat.Users.Db;

[Table("RouletteProfilePrefs")]
public class DbRouletteProfilePrefs : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string CountryCode { get; set; } = "";
    public Gender Gender { get; set; } = Gender.NotSpecified;
    public string Languages { get; set; } = "";
    public string Interests { get; set; } = "";

    public DbRouletteProfilePrefs() { }
    public DbRouletteProfilePrefs(ProfilePreferences model) => UpdateFrom(model);

    public ProfilePreferences ToModel()
    {
        var country = CountryCode.IsNullOrEmpty() ? Country.NotSpecified : new Country(CountryCode);
        ImmutableArray<Language> languages =
            Languages.IsNullOrEmpty()
                ? ImmutableArray<Language>.Empty
                : [..Languages.Split(',').Select(l => new Language(l))];
        ImmutableArray<Interest> interests =
            Interests.IsNullOrEmpty()
                ? ImmutableArray<Interest>.Empty
                : [..Interests.Split(',').Select(i => new Interest(i))];
        return new ProfilePreferences(Id, Version) {
            Preferences = new Preferences {
                Country = country,
                Gender = Gender,
                Languages = languages,
                Interests = interests
            }
        };
    }

    public void UpdateFrom(ProfilePreferences model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;

        var preferences = model.Preferences;
        CountryCode = preferences.Country.Code;
        Gender = preferences.Gender;
        Languages = string.Join(",", preferences.Languages.Select(c => c.Id.Value));
        Interests = string.Join(",", Sort(preferences.Interests).Select(c => c.Code));
    }

    private IEnumerable<Interest> Sort(ImmutableArray<Interest> interests)
        => interests
            .Select(c => {
                var index = Interests.IndexOf(c.Code, StringComparison.Ordinal);
                if (index < 0)
                    index = int.MaxValue;
                return new { Interest = c, index };
            })
            .OrderBy(c => c.index)
            .Select(c => c.Interest);

    internal class EntityConfiguration : IEntityTypeConfiguration<DbRouletteProfilePrefs>
    {
        public void Configure(EntityTypeBuilder<DbRouletteProfilePrefs> builder)
            => builder.Property(a => a.Id).IsRequired();
    }
}
