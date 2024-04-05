using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("ChatCopyStates")]
[Index(nameof(SourceChatId))]
public class DbChatCopyState : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _createdAt;
    private DateTime _lastCopyingAt;
    private DateTime _publishedAt;

    public DbChatCopyState() { }
    public DbChatCopyState(ChatCopyState model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string SourceChatId { get; set; } = null!;

    public bool IsCopiedSuccessfully { get; set; }
    public bool IsPublished { get; set; }
    public long LastEntryId { get; set; }
    public string LastCorrelationId { get; set; } = "";

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime LastCopyingAt {
        get => _lastCopyingAt.DefaultKind(DateTimeKind.Utc);
        set => _lastCopyingAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime PublishedAt {
        get => _publishedAt.DefaultKind(DateTimeKind.Utc);
        set => _publishedAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public ChatCopyState ToModel()
        => new (new ChatId(Id), Version) {
            CreatedAt = CreatedAt,
            SourceChatId = new ChatId(SourceChatId),
            LastCopyingAt = LastCopyingAt,
            LastEntryId = LastEntryId,
            LastCorrelationId = LastCorrelationId,
            IsCopiedSuccessfully = IsCopiedSuccessfully,
            PublishedAt = PublishedAt,
            IsPublished = IsPublished
        };

    public void UpdateFrom(ChatCopyState model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        CreatedAt = model.CreatedAt;
        SourceChatId = model.SourceChatId;
        LastCopyingAt = model.LastCopyingAt;
        LastEntryId = model.LastEntryId;
        LastCorrelationId = model.LastCorrelationId;
        IsCopiedSuccessfully = model.IsCopiedSuccessfully;
        PublishedAt = model.PublishedAt;
        IsPublished = model.IsPublished;
    }
}
