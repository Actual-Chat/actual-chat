namespace ActualChat.Chat;

public sealed record InboundField(string? Title, string? Value);

public sealed record InboundCard(
    string? Title, string? TitleLink, string? Pretext, string? Text,
    InboundField[] Fields, string? ImageUrl, string? ThumbUrl, string? Footer);

public sealed record InboundPayload(string? Text, long? ReplyTo, InboundCard[] Attachments)
{
    public bool IsEmpty
        => Text.IsNullOrWhiteSpace() && Attachments.Length == 0;
}
