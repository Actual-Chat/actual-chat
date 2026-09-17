using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Users.Db;

[Table("Maintenances")]
public class DbMaintenance
{
    [DbKey] public string Id { get; set; } = "";
    public MaintenanceMode Mode { get; set; }
}
