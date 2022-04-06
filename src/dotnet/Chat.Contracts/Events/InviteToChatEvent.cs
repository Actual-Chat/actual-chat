namespace ActualChat.Chat.Events;

public record InviteToChatEvent(Symbol ChatId, string UserId): IChatEvent;
