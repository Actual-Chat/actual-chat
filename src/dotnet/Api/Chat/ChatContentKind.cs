namespace ActualChat.Chat;

// Category of indexed chat content. Each kind maps to a dedicated storage table
// (ChatVisualMediaItems / ChatFileItems / ChatLinkItems) and DTO type
// (VisualMediaItem / FileItem / LinkItem).
public enum ChatContentKind
{
    Media = 0,
    File = 1,
    Link = 2,
}
