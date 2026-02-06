namespace ActualChat.Audio.WebM.Models;

/// <summary>
/// Specifies the type of media track in a WebM file.
/// </summary>
#pragma warning disable CA1028 // If possible, make the underlying enum type System.Int32

[Flags]
public enum TrackType : ulong
{
    Video = 1,
    Audio = 2,
    Complex = 3,
    Logo = 16,
    SubTitle = 17,
    Buttons = 18,
    Control = 32,
    MetaData = 33
}
