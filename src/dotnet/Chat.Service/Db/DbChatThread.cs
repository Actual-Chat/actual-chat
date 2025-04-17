using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Chat.Db;

[Table("ChatThreads")]
[Index(nameof(ParentChatId), nameof(ThreadId))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbChatThread : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _createdAt;

    public DbChatThread() { }
    public DbChatThread(ChatThread model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string ParentChatId { get; set; } = "";
    public ulong ThreadId { get; set; }

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public string Title { get; set; } = "";

    public ChatThread ToModel()
        => new(new ChatId(Id), Version) {
            CreatedAt = CreatedAt,
            Title = Title,
        };

    public void UpdateFrom(ChatThread model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();
        Version = model.Version;
        Title = model.Title;
        if (!Id.IsNullOrEmpty())
            return; // Only the above properties can be changed for already existing threads

        Id = id;
        ParentChatId = id.Parent;
        ThreadId = (ulong)id.ThreadId;
        CreatedAt = model.CreatedAt;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatThread>
    {
        public void Configure(EntityTypeBuilder<DbChatThread> builder)
            => builder.HasIndex(nameof(ParentChatId), nameof(ThreadId))
                .IncludeProperties(nameof(Id));
    }
}
