namespace ActualChat.Audio.WebM.Models
{
    public sealed class MatroskaDocument
    {
        public EBML Ebml { get; set; } = null!;

        public Segment Segment { get; set; } = null!;
    }
}