using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl;

namespace ActualChat.Audio.Db
{
    [Table("AudioSegments")]
    public class DbAudioSegment 
    {
        public string RecordingId { get; set; }
        public int Index { get; set; }
        public string BlobId { get; set; }
        public string BlobMetadata { get; set; }
    }
}
