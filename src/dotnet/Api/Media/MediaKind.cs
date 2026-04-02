namespace ActualChat.Media;

#pragma warning disable CA1027 // Mark enums with FlagsAttribute

public enum MediaKind
{
    Unknown = 0,
    ChatEntryAudio = 0x1,
    ChatEntryVideo = 0x2,
    ChatEntryAttachment = 0x3,
    ChatPicture = 0x10,
    UserPicture = 0x20,
    UserAvatarPicture = 0x21,
    LinkPreviewPicture = 0x30,
}

public static class MediaKindExt
{
    extension(MediaKind mediaKind)
    {
        public bool IsChatIcon => mediaKind is MediaKind.ChatPicture or MediaKind.UserPicture or MediaKind.UserAvatarPicture;
    }
}
