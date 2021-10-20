namespace ActualChat.Db.Module
{
    public class DbSettings
    {
        public string DefaultDb { get; set; } = "memory://ac_{instance_}{context}";
        public string OverrideDb { get; set; } = "";
    }
}
