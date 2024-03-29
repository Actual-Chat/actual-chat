using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Search.Db;

[Table("ContactIndexState")]
public class DbContactIndexState : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private const string IndexSchemaVersionDelimiter = "-";
    public static readonly string UserContactIndexStateId = $"{ElasticNames.UserIndexVersion}{IndexSchemaVersionDelimiter}users";
    public static readonly string ChatContactIndexStateId = $"{ElasticNames.ChatIndexVersion}{IndexSchemaVersionDelimiter}chats";
    public DbContactIndexState() { }
    public DbContactIndexState(ContactIndexState model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = "";

    [ConcurrencyCheck] public long Version { get; set; }
    public string LastUpdatedId { get; set; } = "";
    public long LastUpdatedVersion { get; set; }

    public ContactIndexState ToModel()
        => new (Id, Version) {
            LastUpdatedId = LastUpdatedId,
            LastUpdatedVersion = LastUpdatedVersion,
        };

    public void UpdateFrom(ContactIndexState model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        LastUpdatedId = model.LastUpdatedId;
        LastUpdatedVersion = model.LastUpdatedVersion;
    }
}
