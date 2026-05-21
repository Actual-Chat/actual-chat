namespace ActualChat.Chat;

/// <summary>
/// Category of an indexed <see cref="ChatContentItem"/>. A stored item always has a single
/// category; combined values (e.g. <see cref="Media"/>) are used as query masks.
/// </summary>
[Flags]
public enum ChatContentKind
{
    None = 0,
    Photo = 1,
    Video = 2,
    File = 4,
    Link = 8,
    Media = Photo | Video,
    All = Photo | Video | File | Link,
}
