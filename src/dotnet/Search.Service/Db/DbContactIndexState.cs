using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Search.Db;

[Table("ContactIndexState")]
public class DbContactIndexState : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private const string IndexSchemaVersionDelimiter = "-";
    public static readonly string UserContactIndexId = $"{ElasticNames.UserIndexVersion}{IndexSchemaVersionDelimiter}users";
    public static readonly string ChatContactIndexStateId = $"{ElasticNames.ChatIndexVersion}{IndexSchemaVersionDelimiter}chats";
    private DateTime _lastCreatedAt;
    public DbContactIndexState() { }
    public DbContactIndexState(ContactIndexState model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = "";

    [ConcurrencyCheck] public long Version { get; set; }
    public string LastCreatedId { get; set; } = "";
    public string LastUpdatedId { get; set; } = "";
    public long LastUpdatedVersion { get; set; }

    public DateTime LastCreatedAt {
        get => _lastCreatedAt.DefaultKind(DateTimeKind.Utc);
        set => _lastCreatedAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public ContactIndexState ToModel()
        => new (Id, Version) {
            LastCreatedId = LastCreatedId,
            LastUpdatedId = LastUpdatedId,
            LastCreatedAt = LastCreatedAt,
            LastUpdatedVersion = LastUpdatedVersion,
        };

    public void UpdateFrom(ContactIndexState model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        LastCreatedId = model.LastCreatedId;
        LastUpdatedId = model.LastUpdatedId;
        LastCreatedAt = model.LastCreatedAt;
        LastUpdatedVersion = model.LastUpdatedVersion;
    }
}
