namespace ActualChat.Chat;

// Category of indexed chat content. Each kind maps to a dedicated storage table
// (ChatVisualMediaItems / ChatFileItems / ChatLinkItems) and DTO type
// (VisualMediaItem / FileItem / LinkItem).
public enum ChatContentKind
{
    Media = 1,
    File = 2,
    Link = 3,
}
