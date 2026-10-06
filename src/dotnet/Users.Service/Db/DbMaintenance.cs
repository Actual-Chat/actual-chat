using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Users.Db;

[Table("Maintenances")]
public class DbMaintenance
{
    [DbKey] public string Id { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string StartedBy { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public MaintenanceMode Mode { get; set; }
    // Space-separated; empty means the maintenance covers the key's whole scope
    public string Targets { get; set; } = "";

    public Maintenance ToModel()
        => new(Mode, OwnerId) {
            StartedBy = UserId.ParseNullable(StartedBy),
            StartedAt = StartedAt.DefaultKind(DateTimeKind.Utc),
            Targets = Targets.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToApiArray(),
        };
}
