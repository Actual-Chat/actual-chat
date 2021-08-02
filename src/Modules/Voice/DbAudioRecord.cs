using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl;

namespace ActualChat.Voice
{
    [Table("AudioRecords")]
    public class DbAudioRecord : IHasId<long>
    {
        [Key]
        public long Id { get; set; }
        public byte[] Audio { get; set; } = null!; // AY: I'd go w/ the link to some blob here instead
    }
}
