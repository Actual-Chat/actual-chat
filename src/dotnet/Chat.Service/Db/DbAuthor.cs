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
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbAuthor : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = null!;
    public long LocalId { get; set; }

    public bool IsAnonymous { get; set; }
    public string UserId { get; set; } = "";
    public string? AvatarId { get; set; }
    public bool HasLeft { get; set; }
    public bool IsPlaceAuthor { get; set; }

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }
    public List<DbAuthorRole> Roles { get; } = new();

    public DbAuthor() { }
    public DbAuthor(AuthorFull model) => UpdateFrom(model);

    public AuthorFull ToModel()
    {
        var userId = ActualChat.UserId.Parse(UserId);
        var authorId = AuthorId.Parse(Id);
        var result = new AuthorFull(userId!, authorId, Version) {
            IsAnonymous = IsAnonymous,
            AvatarId = AvatarId ?? "",
            HasLeft = HasLeft,
            RoleIds = Roles.ToArrayOfKnownLength(Roles.Count, x => RoleId.Parse(x.DbRoleId)).SortInPlace(),
            CreatedAt = CreatedAt,
            IsPlaceAuthor = IsPlaceAuthor,
        };
        return result;
    }

    public void UpdateFrom(AuthorFull model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireSomeVersion();

        Id = id.Value;
        Version = model.Version;
        ChatId = id.ChatId.Value;
        LocalId = id.LocalId;
        IsAnonymous = model.IsAnonymous;
        UserId = model.UserId.Value;
        AvatarId = model.AvatarId.NullIfEmpty();
        HasLeft = model.HasLeft;
        IsPlaceAuthor = model.ChatId is PlaceChatId { IsRoot: true };
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
