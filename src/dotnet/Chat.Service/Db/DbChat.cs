using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("Chats")]
[Index(nameof(CreatedAt))]
[Index(nameof(Version))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbChat : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    public DbChat() { }
    public DbChat(Chat model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public ChatKind Kind { get; set; }
    [Obsolete("2023.03: Use MediaId instead.")]
    public string Picture { get; set; } = "";
    public string MediaId { get; set; } = "";
    [Column("UserLinkId")] // TODO(AY): Rename to AliasId
    public string AliasId { get; set; } = "";

    // Template info for embedded chats
    public bool IsTemplate { get; set; }
    public string? TemplateId { get; set; }
    public string? TemplatedForUserId { get; set; }

    // Permissions & Rules
    public bool IsPublic { get; set; }
    public bool IsArchived { get; set; }
    public bool AllowGuestAuthors { get; set; }
    public bool AllowAnonymousAuthors { get; set; }
    public string? SystemTag { get; set; }
    public bool IsPlaceRootChat { get; set; }
    public bool? IsSummarized { get; set; }

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public Chat ToModel()
        => new(ChatId.Parse(Id), Version) {
            Title = Title,
            Description = Description,
            CreatedAt = CreatedAt,
            IsTemplate = IsTemplate,
            TemplateId = TemplateId.IsNullOrEmpty()
                ? null
                : ChatId.Parse(TemplateId),
            TemplatedForUserId = TemplatedForUserId.IsNullOrEmpty()
                ? null
                : UserId.Parse(TemplatedForUserId),
            IsPublic = IsPublic,
            IsArchived = IsArchived,
            AllowGuestAuthors = AllowGuestAuthors,
            AllowAnonymousAuthors = AllowAnonymousAuthors,
            SystemTag = SystemTag.IsNullOrEmpty()
                ? Symbol.Empty
                : new Symbol(SystemTag),
            MediaId = ActualChat.MediaId.ParseNullable(MediaId),
            AliasId = ActualChat.AliasId.ParseNullable(AliasId),
            IsSummarized = IsSummarized,
        };

    public void UpdateFrom(Chat model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireSomeVersion();

        Id = id.Value;
        Version = model.Version;
        Title = model.Title;
        Description = model.Description;
        CreatedAt = model.CreatedAt;
        IsTemplate = model.IsTemplate;
        TemplateId = model.TemplateId?.Value;
        TemplatedForUserId = model.TemplatedForUserId?.Value;
        IsPublic = model.IsPublic;
        IsArchived = model.IsArchived;
        AllowGuestAuthors = model.AllowGuestAuthors;
        AllowAnonymousAuthors = model.AllowAnonymousAuthors;
        SystemTag = model.SystemTag.IsEmpty
            ? null
            : model.SystemTag.Value;
        Kind = model.Kind;
        MediaId = model.MediaId?.Value ?? "";
        IsPlaceRootChat = model.Id is PlaceChatId { IsRoot: true };
        AliasId = model.AliasId?.NormalizedValue ?? "";
        IsSummarized = model.IsSummarized;
    }
}
