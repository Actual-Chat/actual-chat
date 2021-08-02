using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl;

namespace ActualChat.Voice
{
    [Table("VoiceRecords")]
    public class DbVoiceRecord : IHasId<long>
    {
        [Key]
        public long Id { get; init; }
        
        public byte[] AudioData { get; init; }
    }
}