namespace ActualChat.Chat;

public static class MarkupConsumerExt
{
    public static int? GetTrimLength(this MarkupConsumer consumer)
        => consumer switch {
            MarkupConsumer.QuoteView => 300,
            MarkupConsumer.ChatListItemText => 100,
            MarkupConsumer.Notification => 100,
            MarkupConsumer.ReactionNotification => 30,
            _ => null,
        };
}
