using Stl.DependencyInjection;

namespace ActualChat.Host
{
    [RegisterSettings("ActualChat")]
    public class HostSettings
    {
        public string PublisherId { get; set; } = "p";
    }
}
