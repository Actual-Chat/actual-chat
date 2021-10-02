namespace ActualChat.Audio;

public class AudioSettings
{
    public string Db { get; set; } =
        "Server=localhost;Database=ac_dev_audio;Port=5432;User Id=postgres;Password=ActualChat.Dev.2021.07";
    public string Redis { get; set; } = "localhost";
    public bool UseMessagePackWithSignalR { get; set; } = false;
}
