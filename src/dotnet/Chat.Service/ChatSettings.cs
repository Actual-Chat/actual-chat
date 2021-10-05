using Stl.DependencyInjection;

namespace ActualChat.Chat
{
    public class ChatSettings
    {
        public string Db { get; set; } = null!;

        public string DefaultChatId { get; set; } = null!;
    }
}
