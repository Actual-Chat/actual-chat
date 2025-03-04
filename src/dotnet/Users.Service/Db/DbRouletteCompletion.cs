using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Roulette;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;

namespace ActualChat.Users.Db;

[Table("RouletteCompletions")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbRouletteCompletion : IHasId<string>, IHasVersion<long>
{
    private DateTime _completedAt;

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string OwnerUserId { get; set; } = "";
    public string OwnerProfileId { get; set; } = "";
    public string PeerUserId { get; set; } = "";
    public string PeerProfileId { get; set; } = "";
    public CompleteChatRouletteReason CompleteReason { get; set; }
    public bool IsInitiatedByOwner { get; set; }
    public DateTime CompletedAt {
        get => _completedAt.DefaultKind(DateTimeKind.Utc);
        set => _completedAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbRouletteCompletion() { }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbRouletteCompletion>
    {
        public void Configure(EntityTypeBuilder<DbRouletteCompletion> builder)
            => builder.Property(a => a.Id).IsRequired();
    }
}
