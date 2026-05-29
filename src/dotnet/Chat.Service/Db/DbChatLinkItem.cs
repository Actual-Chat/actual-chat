using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("ChatLinkItems")]
[Index(nameof(ChatId), nameof(At), nameof(EntryLocalId), nameof(LocalIndex))]
[Index(nameof(EntryId))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbChatLinkItem : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private const char IdSeparator = ':';

    public DbChatLinkItem() { }
    public DbChatLinkItem(LinkItem model) => UpdateFrom(model);

    [DbKey] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = "";
    public string EntryId { get; set; } = "";
    public long EntryLocalId { get; set; }
    public int LocalIndex { get; set; }

    public DateTime At {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public string Url { get; set; } = "";
    public string LinkPreviewId { get; set; } = "";

    public static string ComposeId(ChatEntryId entryId, int localIndex)
        => $"{entryId}{IdSeparator}{localIndex}";

    public LinkItem ToModel()
        => new() {
            Id = Id,
            Version = Version,
            EntryId = ChatEntryId.Parse(EntryId),
            LocalIndex = LocalIndex,
            At = new Moment(At),
            Url = Url,
            LinkPreviewId = LinkPreviewId,
        };

    public void UpdateFrom(LinkItem model)
    {
        var id = ComposeId(model.EntryId, model.LocalIndex);
        this.RequireSameOrEmptyId(id);

        Id = id;
        Version = model.Version;
        ChatId = model.EntryId.ChatId.Value;
        EntryId = model.EntryId.Value;
        EntryLocalId = model.EntryId.LocalId;
        LocalIndex = model.LocalIndex;
        At = model.At.ToDateTime();
        Url = model.Url;
        LinkPreviewId = model.LinkPreviewId.Value;
    }
}
