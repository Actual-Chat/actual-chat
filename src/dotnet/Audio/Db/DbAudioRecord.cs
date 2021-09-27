using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl;
using Stl.Time;

namespace ActualChat.Audio.Db
{
    [Table("AudioRecordings")]
    public class DbAudioRecord : IHasId<string>
    {
        private DateTime _beginsAt;
        private DateTime? _endsAt;

        // TODO(AY): Add CompositeId
        [Key]
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string ChatId { get; set; } = string.Empty;

        public DateTime BeginsAt {
            get => _beginsAt.DefaultKind(DateTimeKind.Utc);
            set => _beginsAt = value.DefaultKind(DateTimeKind.Utc);
        }

        public DateTime? EndsAt {
            get => _endsAt.DefaultKind(DateTimeKind.Utc);
            set => _endsAt = value.DefaultKind(DateTimeKind.Utc);
        }

        public double? Duration { get; set; }

        public AudioCodec AudioCodec { get; set; }
        public int ChannelCount { get; set; }
        public int SampleRate { get; set; }
        [StringLength(5)]
        public string Language { get; set; } = "en-us";

        public List<DbAudioSegment> Segments { get; set; } = new();
    }
}
