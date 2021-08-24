using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Stl;
using Stl.Time;

namespace ActualChat.Chat.Db
{
    [Table("ChatEntries")]
    [Index(nameof(ChatId), nameof(BeginsAt), nameof(EndsAt), nameof(ContentType))]
    [Index(nameof(ChatId), nameof(EndsAt), nameof(BeginsAt), nameof(ContentType))]
    public class DbChatEntry : IHasId<long>
    {
        private DateTime _beginsAt;
        private DateTime _endsAt;

        public string ChatId { get; set; } = "";
        public long Id { get; set; } = 0;
        public string CreatorId { get; set; } = "";

        public DateTime BeginsAt {
            get => _beginsAt.DefaultKind(DateTimeKind.Utc);
            set => _beginsAt = value.DefaultKind(DateTimeKind.Utc);
        }

        public DateTime EndsAt {
            get => _endsAt.DefaultKind(DateTimeKind.Utc);
            set => _endsAt = value.DefaultKind(DateTimeKind.Utc);
        }

        public double Duration { get; set; }

        public ChatContentType ContentType { get; set; }
        public string Content { get; set; } = "";
        public string? RecordingId { get; set; }

        public ChatEntry ToModel()
            => new(ChatId, Id) {
                CreatorId = CreatorId,
                BeginsAt = BeginsAt,
                EndsAt = EndsAt,
                ContentType = ContentType,
                Content = Content,
                RecordingId = RecordingId ?? "",
            };
    }
}
