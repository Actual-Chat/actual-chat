﻿using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("TextEntryAttachments")]
public class DbTextEntryAttachment : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private const char IdSeparator = ':';
    public DbTextEntryAttachment() { }
    public DbTextEntryAttachment(TextEntryAttachment model) => UpdateFrom(model);

    // (ChatId, EntryId, Index)
    [Key] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string EntryId { get; set; } = "";
    public string MediaId { get; set; } = "";
    public string ThumbnailMediaId { get; set; } = "";
    public int Index { get; set; }

    [Obsolete("2023.03: Use MediaId instead.")]
    public string ContentId { get; set; } = "";
    [Obsolete("2023.03: Use MediaId instead.")]
    public string MetadataJson { get; set; } = "";

    public static string ComposeId(TextEntryId entryId, int index)
        => $"{entryId}{IdSeparator}{index}";

    public static string IdPrefix(TextEntryId entryId)
        => entryId + IdSeparator;

    public TextEntryAttachment ToModel()
        => new (Id, Version) {
            EntryId = new TextEntryId(EntryId),
            Index = Index,
            MediaId = new MediaId(MediaId),
            ThumbnailMediaId = new MediaId(ThumbnailMediaId),
        };

    public void UpdateFrom(TextEntryAttachment model)
    {
        var id = ComposeId(model.EntryId, model.Index);
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        EntryId = model.EntryId;
        Index = model.Index;
        MediaId = model.MediaId;
        ThumbnailMediaId = model.ThumbnailMediaId;
    }
}
