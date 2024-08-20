using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("Authors")]
[Index(nameof(ChatId), nameof(LocalId), IsUnique = true)]
[Index(nameof(ChatId), nameof(UserId), IsUnique = true)]
[Index(nameof(UserId), nameof(AvatarId))]
[Index(nameof(Version), nameof(IsPlaceAuthor))]
public class DbAuthor : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _createdAt;
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = null!;
    public long LocalId { get; set; }

    public bool IsAnonymous { get; set; }
    public string? UserId { get; set; }
    public string? AvatarId { get; set; }
    public bool HasLeft { get; set; }
    public bool IsPlaceAuthor { get; set; }

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }
    public List<DbAuthorRole> Roles { get; } = new();

    public DbAuthor() { }
    public DbAuthor(AuthorFull model) => UpdateFrom(model);

    public AuthorFull ToModel()
    {
        var result = new AuthorFull(new AuthorId(Id), Version) {
            IsAnonymous = IsAnonymous,
            UserId = new UserId(UserId ?? Symbol.Empty, AssumeValid.Option),
            AvatarId = AvatarId ?? "",
            HasLeft = HasLeft,
            RoleIds = new ApiArray<Symbol>(Roles.ToArray(Roles.Count, x => (Symbol)x.DbRoleId).SortInPlace()),
            CreatedAt = CreatedAt,
            IsPlaceAuthor = IsPlaceAuthor,
        };
        return result;
    }

    public void UpdateFrom(AuthorFull model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        ChatId = id.ChatId;
        LocalId = id.LocalId;
        IsAnonymous = model.IsAnonymous;
        UserId = model.UserId.Value.NullIfEmpty();
        AvatarId = model.AvatarId.NullIfEmpty();
        HasLeft = model.HasLeft;
        IsPlaceAuthor = model.ChatId.IsPlaceRootChat;
        CreatedAt = model.CreatedAt.ToDateTimeClamped();
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbAuthor>
    {
        public void Configure(EntityTypeBuilder<DbAuthor> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.ChatId).IsRequired();
            builder.HasAnnotation(nameof(ConflictStrategy), ConflictStrategy.DoNothing);
            // builder.HasMany(a => a.Roles).WithOne();
        }
    }
}
