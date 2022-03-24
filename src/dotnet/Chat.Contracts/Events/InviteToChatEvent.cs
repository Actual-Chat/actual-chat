namespace ActualChat.Chat.Events;

public record InviteToChatEvent(string ChatId, string UserId): IChatEvent;
