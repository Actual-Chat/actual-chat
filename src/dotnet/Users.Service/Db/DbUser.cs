using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;

namespace ActualChat.Users.Db;

[Table("Users")]
[Index(nameof(Name))]
public class DbUser : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private NewtonsoftJsonSerialized<ImmutableDictionary<string, string>> _claims
        = ImmutableDictionary<string, string>.Empty;

    [Key] public string Id { get; set; } = null!;

    [ConcurrencyCheck]
    public long Version { get; set; }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "We assume server-side code is fully preserved")]
    [MinLength(3)]
    public string Name { get; set; } = "";

    public string ClaimsJson {
        get => _claims.Data;
        set => _claims = value;
    }

    [NotMapped]
    public ImmutableDictionary<string, string> Claims {
        get => _claims.Value;
        set => _claims = value;
    }

    public List<DbUserIdentity<string>> Identities { get; } = new();

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    } = CoarseSystemClock.Instance.Now;

    public User ToModel()
        => new(Id ?? "", Name) {
            Version = Version,
            Claims = Claims.ToApiMap(),
            Identities = Identities.ToApiMap(
                ui => new UserIdentity(ui.Id),
                ui => ui.Secret)
        };

    public void UpdateFrom(User source, VersionGenerator<long> versionGenerator)
    {
        var targetId = Id ?? "";
        if (!string.Equals(targetId, source.Id, StringComparison.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(source));
        Version = versionGenerator.NextVersion(Version);

        // Add + update claims
        Claims = Claims.SetItems(source.Claims);

        // Add + update identities
        var identities = Identities.ToDictionary(ui => ui.Id, StringComparer.Ordinal);
        foreach (var (userIdentity, secret) in source.Identities) {
            if (!userIdentity.IsValid)
                continue;
            var foundIdentity = identities.GetValueOrDefault(userIdentity.Id);
            if (foundIdentity is not null) {
                foundIdentity.Secret = secret;
                continue;
            }
            Identities.Add(new DbUserIdentity<string>() {
                Id = userIdentity.Id,
                DbUserId = Id!, // Already validated Id matches source.Id
                Secret = secret ?? "",
            });
        }
    }
}
