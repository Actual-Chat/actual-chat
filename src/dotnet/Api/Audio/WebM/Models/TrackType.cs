namespace ActualChat.Audio.WebM.Models;

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
