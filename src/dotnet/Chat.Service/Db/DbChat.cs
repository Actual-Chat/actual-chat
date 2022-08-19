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
    public ChatType ChatType { get; set; }
    public string Picture { get; set; } = "";

    // Permissions & Rules
    public bool IsPublic { get; set; }

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public List<DbChatOwner> Owners { get; set; } = new();

    public Chat ToModel()
        => new() {
            Id = Id,
            Version = Version,
            Title = Title,
            CreatedAt = CreatedAt,
            IsPublic = IsPublic,
            ChatType = ChatType,
            Picture = Picture,
        };

    public void UpdateFrom(Chat model)
    {
        Id = model.Id;
        Version = model.Version;
        Title = model.Title;
        CreatedAt = model.CreatedAt;
        IsPublic = model.IsPublic;
        ChatType = model.ChatType;
        Picture = model.Picture;
    }
}
