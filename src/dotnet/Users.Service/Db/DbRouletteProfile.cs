using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Roulette;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;

namespace ActualChat.Users.Db;

[Table("RouletteProfilePrefs")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbRouletteProfilePrefs : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string UserId { get; set; } = "";
    public string Country { get; set; } = "";
    public Gender Gender { get; set; } = Gender.Undefined;
    public string Languages { get; set; } = "";
    public string Interests { get; set; } = "";

    public DbRouletteProfilePrefs() { }
    public DbRouletteProfilePrefs(ProfilePreferencesFull model) => UpdateFrom(model);

    public ProfilePreferencesFull ToModel()
    {
        var country = ActualChat.Country.Parse(Country ?? "");
        var languages = Languages.IsNullOrEmpty()
            ? Array.Empty<Language>()
            : [..Languages.Split(',').Select(Language.Parse)];
        var interests = Interests.IsNullOrEmpty()
            ? Array.Empty<Interest>()
            : [..Interests.Split(',').Select(Interest.Parse)];
        var userId = ActualChat.UserId.Parse(UserId);
        return new ProfilePreferencesFull(userId, Id, Version) {
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
        if (UserId.IsNullOrEmpty())
            UserId = model.UserId.Value;
        else if (!OrdinalEquals(UserId, model.UserId.Value))
            throw StandardError.Constraint("UserId can't be changed.");

        var preferences = model.Preferences;
        Country = preferences.Country.Value;
        Gender = preferences.Gender;
        Languages = preferences.Languages.Select(c => c.Value).ToDelimitedString(",");
        Interests = Sort(preferences.Interests).Select(c => c.Value).ToDelimitedString(",");
    }

    private IEnumerable<Interest> Sort(IEnumerable<Interest> interests)
        => interests
            .Select(c => {
                var index = Interests.OrdinalIndexOf(c.Value);
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
