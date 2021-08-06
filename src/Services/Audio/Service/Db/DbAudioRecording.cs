using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl;

namespace ActualChat.Audio.Db
{
    [Table("AudioRecordings")]
    public class DbAudioRecording : IHasId<string>
    {
        [Key]
        public string Id { get; set; } = string.Empty;
        
        public string UserId { get; set; } = string.Empty;
        
        // We need chat reference there
        
        public DateTime RecordingStartedUtc { get; set; }
        
        public double RecordingDuration { get; set; }
        
        public AudioCodec AudioCodec { get; set; }
        
        public int ChannelCount { get; set; }
        
        public int SampleRate { get; set; }
        
        [StringLength(5)]
        public string Language { get; set; } = "en-us";
        
        public List<DbAudioSegment> Segments { get; set; } = new();
    }
}