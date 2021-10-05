using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl;

namespace ActualChat.Audio.Db
{
    [Table("AudioSegments")]
    public class DbAudioSegment
    {
        public string RecordId { get; set; } = string.Empty;
        public int Index { get; set; }
        public TimeSpan Offset { get; set; }
        public TimeSpan Duration { get; set; }
        public string BlobId { get; set; } = string.Empty;
        public string Metadata { get; set; } = string.Empty;

        public DbAudioRecord AudioRecord { get; set; } = null!;
    }
}
