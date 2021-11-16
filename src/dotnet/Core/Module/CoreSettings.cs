namespace ActualChat.Module;

public class CoreSettings
{
    public const string DisabledGoogleStorageBucket = "none";

    public string Instance { get; set; } = "dev";
    public string GoogleStorageBucket { get; set; } = DisabledGoogleStorageBucket;
}
