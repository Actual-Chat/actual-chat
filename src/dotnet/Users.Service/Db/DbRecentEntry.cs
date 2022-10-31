using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;

namespace ActualChat.Users.Db;

public class DbRecentEntry : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _updatedAt;

    [Key] public string Id { get; set; } = null!;
    public string ShardKey { get; set; } = null!;
    public string Key { get; set; } = null!;
    public string Scope { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public DateTime UpdatedAt {
        get => _updatedAt.DefaultKind(DateTimeKind.Utc);
        set => _updatedAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbRecentEntry() { }
    public DbRecentEntry(RecentEntry recentEntry) => UpdateFrom(recentEntry);

    public RecentEntry ToModel()
        => new(ShardKey, Key, Enum.Parse<RecencyScope>(Scope)) {
            Version = Version,
            UpdatedAt = UpdatedAt,
        };

    public void UpdateFrom(RecentEntry model)
    {
        Id = GetId(model.GroupKey, model.Key);
        ShardKey = model.GroupKey;
        Key = model.Key;
        Scope = model.Scope.ToString();
        UpdatedAt = model.UpdatedAt;
        Version = model.Version;
    }

    public static string GetId(string shardKey, string key)
        => $"{shardKey}:{key}";


    internal class EntityConfiguration : IEntityTypeConfiguration<DbRecentEntry>
    {
        public void Configure(EntityTypeBuilder<DbRecentEntry> builder)
            => builder.Property(a => a.Id).IsRequired();
    }
}
