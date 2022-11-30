using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;

namespace ActualChat.Chat.Db;

[Table("Authors")]
[Index(nameof(ChatId), nameof(LocalId))]
[Index(nameof(ChatId), nameof(UserId))]
public class DbAuthor : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = null!;
    public long LocalId { get; set; }

    public bool IsAnonymous { get; set; }
    public string? UserId { get; set; }
    public string? AvatarId { get; set; }
    public bool HasLeft { get; set; }

    public List<DbAuthorRole> Roles { get; } = new();

    public static string ComposeId(ChatId chatId, long localId)
        => new AuthorId(chatId, localId, AssumeValid.Option).Id;

    public AuthorFull ToModel()
    {
        var result = new AuthorFull(new AuthorId(Id), Version) {
            Version = Version,
            IsAnonymous = IsAnonymous,
            UserId = new UserId(UserId ?? Symbol.Empty, AssumeValid.Option),
            AvatarId = AvatarId ?? "",
            HasLeft = HasLeft,
            RoleIds = Roles.Select(ar => (Symbol)ar.DbRoleId).ToImmutableArray(),
        };
        return result;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbAuthor>
    {
        public void Configure(EntityTypeBuilder<DbAuthor> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.ChatId).IsRequired();
            // builder.HasMany(a => a.Roles).WithOne();
        }
    }
}
