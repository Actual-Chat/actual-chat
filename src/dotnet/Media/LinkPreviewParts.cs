namespace ActualChat.Media;

[Flags]
public enum LinkPreviewParts : byte
{
    None = 0,
    Image = 0x1,
    Title = 0x2,
    Description = 0x4,
    Full = Image | Title | Description,
}
