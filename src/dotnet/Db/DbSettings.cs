namespace ActualChat.Db
{
    public class DbSettings
    {
        public string DefaultDb { get; set; } = "memory://{dbName}";
        public string OverrideDb { get; set; } = "";
    }
}
