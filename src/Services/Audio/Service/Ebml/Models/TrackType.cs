using System;

namespace ActualChat.Audio.Ebml.Models
{
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
}