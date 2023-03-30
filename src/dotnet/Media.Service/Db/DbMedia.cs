﻿using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Media.Db;

[Table("Media")]
public class DbMedia : IHasId<string>, IRequirementTarget
{
    public DbMedia() { }
    public DbMedia(Media model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = "";

    public string ContentId { get; set; } = "";
    public string MetadataJson { get; set; } = "";

    public Media ToModel()
        => new (new MediaId(Id)) {
            ContentId = ContentId,
            MetadataJson = MetadataJson,
        };

    public void UpdateFrom(Media model)
    {
        this.RequireSameOrEmptyId(model.Id);

        if (!Id.IsNullOrEmpty())
            return;

        Id = model.Id;
        ContentId = model.ContentId;
        MetadataJson = model.MetadataJson;
    }
}
