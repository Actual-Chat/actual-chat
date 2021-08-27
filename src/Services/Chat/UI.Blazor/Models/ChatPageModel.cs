using System;

namespace ActualChat.Chat.UI.Blazor.Models
{
    public record ChatPageModel
    {
        public bool IsUnavailable { get; init; }
        public bool MustLogin { get; init; }

        public Chat? Chat { get; init; }
        public ChatEntry[] Entries { get; init; } = Array.Empty<ChatEntry>();
    }
}
