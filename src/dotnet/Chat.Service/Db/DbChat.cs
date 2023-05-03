using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;

namespace ActualChat.Chat.Db;

[Table("Chats")]
public class DbChat : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _createdAt;

    public DbChat() { }
    public DbChat(Chat model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string Title { get; set; } = "";
    public ChatKind Kind { get; set; }
    [Obsolete("Use MediaId")]
    public string Picture { get; set; } = "";
    public string MediaId { get; set; } = "";

    // Permissions & Rules
    public bool IsPublic { get; set; }

    public bool IsTemplate { get; set; }

    public bool AllowGuestAuthors { get; set; }
    public bool AllowAnonymousAuthors { get; set; }

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public Chat ToModel()
        => new(new ChatId(Id), Version) {
            Title = Title,
            CreatedAt = CreatedAt,
            IsPublic = IsPublic,
            IsTemplate = IsTemplate,
            AllowGuestAuthors = AllowGuestAuthors,
            AllowAnonymousAuthors = AllowAnonymousAuthors,
            MediaId = new MediaId(MediaId),
        };

    public void UpdateFrom(Chat model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        Title = model.Title;
        CreatedAt = model.CreatedAt;
        IsPublic = model.IsPublic;
        IsTemplate = model.IsTemplate;
        AllowGuestAuthors = model.AllowGuestAuthors;
        AllowAnonymousAuthors = model.AllowAnonymousAuthors;
        Kind = model.Kind;
        MediaId = model.MediaId;
    }
}
