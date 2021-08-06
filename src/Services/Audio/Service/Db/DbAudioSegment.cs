using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl;

namespace ActualChat.Audio.Db
{
    [Table("AudioSegments")]
    public class DbAudioSegment 
    {
        public string RecordingId { get; set; } = string.Empty;
        public int Index { get; set; }
        public string BlobId { get; set; } = string.Empty;
        public string BlobMetadata { get; set; } = string.Empty;
    }
}
