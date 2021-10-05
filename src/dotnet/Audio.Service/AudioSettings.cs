namespace ActualChat.Audio;

public class AudioSettings
{
    public string Db { get; set; } = null!;
    public string Redis { get; set; } = null!;
    public bool UseMessagePackWithSignalR { get; set; }
}
