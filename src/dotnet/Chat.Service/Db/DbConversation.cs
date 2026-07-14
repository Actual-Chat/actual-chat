using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("Conversations")]
[Index(nameof(Version))]
public class DbConversation : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    public DbConversation() { }
    public DbConversation(Conversation model) => UpdateFrom(model);

    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string ChatId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Summary { get; set; } = "";
    public long StartEntryLid { get; set; }
    public long EndEntryLid { get; set; }
    public int MessageCount { get; set; }
    public int AttachmentCount { get; set; }
    public DateTime StartsAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime EndsAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }
    public string AuthorIds { get; set; } = "[]";
    public string AttachmentIds { get; set; } = "[]";
    public bool IsExpandedByDefault { get; set; }

    public Conversation ToModel()
    {
        var authorIds = JsonSerializer.Deserialize<AuthorId[]>(AuthorIds) ?? [];
        var attachmentIds = JsonSerializer.Deserialize<Symbol[]>(AttachmentIds) ?? [];

        return new Conversation(ConversationId.Parse(Id), Version) {
            Title = Title,
            Description = Description,
            Summary = Summary,
            EndEntryLid = EndEntryLid,
            StartsAt = new Moment(StartsAt),
            EndsAt = new Moment(EndsAt),
            MessageCount = MessageCount,
            AuthorIds = authorIds,
            AttachmentCount = AttachmentCount,
            AttachmentIds = attachmentIds,
            IsExpandedByDefault = IsExpandedByDefault,
        };
    }

    public void UpdateFrom(Conversation model)
    {
        this.RequireSameOrEmptyId(model.Id.Value);
        model.RequireVersion();

        Id = model.Id.Value;
        ChatId = model.Id.ChatId.Value;
        Version = model.Version;
        Title = model.Title;
        Description = model.Description;
        Summary = model.Summary;
        StartEntryLid = model.Id.StartEntryLid;
        EndEntryLid = model.EndEntryLid;
        StartsAt = model.StartsAt.ToDateTime();
        EndsAt = model.EndsAt.ToDateTime();
        MessageCount = model.MessageCount;
        AuthorIds = JsonSerializer.Serialize(model.AuthorIds);
        AttachmentCount = model.AttachmentCount;
        AttachmentIds = JsonSerializer.Serialize(model.AttachmentIds);
        IsExpandedByDefault = model.IsExpandedByDefault;
    }
}
