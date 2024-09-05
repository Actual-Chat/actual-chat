using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.MLSearch.Engine;
using ActualChat.Search;
using ActualLab.Versioning;

namespace ActualChat.MLSearch.Db;

[Table("ContactIndexState")]
public class DbContactIndexState : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private const string IndexSchemaVersionDelimiter = "-";
    public static readonly string UserContactIndexStateId = $"{OpenSearchNames.UserIndexVersion}{IndexSchemaVersionDelimiter}users";
    public static readonly string PlaceAuthorIndexStateId = $"v1{IndexSchemaVersionDelimiter}place-authors";
    public static readonly string ChatContactIndexStateId = $"{OpenSearchNames.GroupIndexVersion}{IndexSchemaVersionDelimiter}chats";
    public static readonly string PlaceContactIndexStateId = $"{OpenSearchNames.PlaceIndexVersion}{IndexSchemaVersionDelimiter}places";

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
