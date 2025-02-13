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

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Summary { get; set; } = "";
    public long StartEntryLid { get; set; }
    public long EndEntryLid { get; set; }
    public int MessageCount { get; set; }
    public DateTime Start {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime End {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }
    public string AuthorIds { get; set; } = "[]";

    public Conversation ToModel()
    {
        var authorIds = JsonSerializer.Deserialize<ApiArray<AuthorId>>(AuthorIds);

        return new Conversation(new ConversationId(Id), Version)
        {
            Title = Title,
            Description = Description,
            Summary = Summary,
            StartEntryLid = StartEntryLid,
            EndEntryLid = EndEntryLid,
            Start = new Moment(Start),
            End = new Moment(End),
            MessageCount = MessageCount,
            AuthorIds = authorIds,
        };
    }

    public void UpdateFrom(Conversation model)
    {
        this.RequireSameOrEmptyId(model.Id);
        model.RequireSomeVersion();

        Id = model.Id.Value;
        Version = model.Version;
        Title = model.Title;
        Description = model.Description;
        Summary = model.Summary;
        StartEntryLid = model.StartEntryLid;
        EndEntryLid = model.EndEntryLid;
        Start = model.Start.ToDateTime();
        End = model.End.ToDateTime();
        MessageCount = model.MessageCount;
        AuthorIds = JsonSerializer.Serialize(model.AuthorIds);
    }

}
