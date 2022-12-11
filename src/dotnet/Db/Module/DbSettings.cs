namespace ActualChat.Db.Module;

public sealed class DbSettings
{
    public string DefaultDb { get; set; } = "memory:ac_{instance_}{context}";
    public string OverrideDb { get; set; } = "";
    public bool ShouldRecreateDb { get; set; } = false;
    public bool ShouldVerifyDb { get; set; } = true;
    public bool ShouldMigrateDb { get; set; } = true;
}
