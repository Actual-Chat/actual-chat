using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;

namespace ActualChat.Feedback.Db;

[Table("FeatureRequests")]
public class DbFeatureRequest : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _createdAt;

    public DbFeatureRequest() { }

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string UserId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string FeatureName { get; set; } = "";
    public int Rating { get; set; }
    public string Comment { get; set; } = "";

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }
}
