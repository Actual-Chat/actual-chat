using Stl.DependencyInjection;

namespace ActualChat.Voice
{
    [RegisterSettings("ActualChat.Audio")]
    public class AudioSettings
    {
        public string Db { get; set; } =
            "Server=localhost;Database=ac_dev_voice;Port=5432;User Id=postgres;Password=ActualChat.Dev.2021.07";
    }
}