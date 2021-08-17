using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Stl;
using Stl.Time;

namespace ActualChat.Chat.Db
{
    [Table("ChatEntries")]
    [Index(nameof(ChatId), nameof(BeginsAt))]
    [Index(nameof(UserId), nameof(BeginsAt), nameof(ChatId))]
    [Index(nameof(UserId), nameof(ChatId), nameof(BeginsAt))]
    [Index(nameof(ChatId), nameof(Id), nameof(BeginsAt))]
    [Index(nameof(UserId), nameof(Id), nameof(BeginsAt), nameof(ChatId))]
    [Index(nameof(UserId), nameof(ChatId), nameof(Id), nameof(BeginsAt))]
    public class DbChatEntry : IHasId<long>
    {
        private DateTime _beginsAt;
        private DateTime _endsAt;

        public long Id { get; set; } = 0;
        public string ChatId { get; set; } = "";
        public bool IsRemoved { get; set; }

        public ChatEntryKind Kind { get; set; }
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

        public string Content { get; set; } = "";

        public ChatEntry ToModel()
            => new(Kind, Id) {
                IsRemoved = IsRemoved,
                UserId = UserId,
                BeginsAt = BeginsAt,
                EndsAt = EndsAt,
                Content = Content,
            };

        public void UpdateFrom(ChatEntry model)
        {
            if (model.Id != 0)
                Id = model.Id;
            Kind = model.Kind;
            IsRemoved = model.IsRemoved;
            UserId = model.UserId;
            BeginsAt = model.BeginsAt;
            EndsAt = model.EndsAt;
            Duration = model.Duration;
            Content = model.Content;
        }
    }
}
