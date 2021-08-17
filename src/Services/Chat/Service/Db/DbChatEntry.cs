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

        public long Id { get; set; } = 0;
        public string ChatId { get; set; } = "";

        public string UserId { get; set; } = "";

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

        public ChatEntry ToModel()
            => new(Id) {
                UserId = UserId,
                BeginsAt = BeginsAt,
                EndsAt = EndsAt,
                ContentType = ContentType,
                Content = Content,
            };

        public void UpdateFrom(ChatEntry model)
        {
            if (model.Id != 0)
                Id = model.Id;
            UserId = model.UserId;
            BeginsAt = model.BeginsAt;
            EndsAt = model.EndsAt;
            Duration = model.Duration;
            ContentType = model.ContentType;
            Content = model.Content;
        }
    }
}
