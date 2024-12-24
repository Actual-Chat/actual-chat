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

    public string UserId { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public Gender Gender { get; set; } = Gender.NotSpecified;
    public string Languages { get; set; } = "";
    public string Interests { get; set; } = "";

    public DbRouletteProfilePrefs() { }
    public DbRouletteProfilePrefs(ProfilePreferencesFull model) => UpdateFrom(model);

    public ProfilePreferencesFull ToModel()
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
        return new ProfilePreferencesFull(new UserId(UserId), Id, Version) {
            Preferences = new Preferences {
                Country = country,
                Gender = Gender,
                Languages = languages,
                Interests = interests
            }
        };
    }

    public void UpdateFrom(ProfilePreferencesFull model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        if (UserId.IsNullOrEmpty()) {
            model.UserId.Require(nameof(ProfilePreferencesFull.UserId));
            UserId = model.UserId;
        }
        else if (!model.UserId.IsNone && !Equals(UserId, model.UserId.Value))
                throw StandardError.Constraint("UserId can't be changed.");

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
