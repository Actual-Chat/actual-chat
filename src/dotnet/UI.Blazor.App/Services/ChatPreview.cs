namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// A chat's last-entry preview: the readable text shown in list items, plus the thread info
/// when the last entry starts a thread. Split out of <see cref="ChatInfo"/> so that parsing
/// markup and resolving threads happens per visible row rather than per contact.
/// </summary>
public sealed record ChatPreview
{
    public static readonly ChatPreview None = new();

    public string Text { get; init; } = "";
    public Chat.Chat? Thread { get; init; }
    public Author? ThreadCreator { get; init; }

    public Moment? ThreadCreatedAt => Thread?.CreatedAt;
}
