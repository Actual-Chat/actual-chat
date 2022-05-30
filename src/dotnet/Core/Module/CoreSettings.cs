namespace ActualChat.Module;

public class CoreSettings
{
    public string Instance { get; set; } = "dev";
    public string GoogleStorageBucket { get; set; } = "";
    public bool UseMediaServer { get; set; }
    public bool UseCdnServer { get; set; }
}
