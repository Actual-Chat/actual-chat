using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Cysharp.Text;
using Microsoft.EntityFrameworkCore;
using Stl.Versioning;

namespace ActualChat.Chat.Db
{
    [Table("ChatEntries")]
    [Index(nameof(ChatId), nameof(Id))]
    [Index(nameof(ChatId), nameof(BeginsAt), nameof(EndsAt), nameof(ContentType))]
    [Index(nameof(ChatId), nameof(EndsAt), nameof(BeginsAt), nameof(ContentType))]
    [Index(nameof(ChatId), nameof(Version))]
    public class DbChatEntry : IHasId<long>, IHasVersion<long>
    {
        public static string GetCompositeId(string chatId, long id)
            => ZString.Format("{0}:{1}", chatId, id);

        private DateTime _beginsAt;
        private DateTime? _endsAt;

        [Key]
        public string CompositeId { get; set; } = "";
        public string ChatId { get; set; } = "";
        public long Id { get; set; }
        [ConcurrencyCheck] public long Version { get; set; }
        public string AuthorId { get; set; } = "";

        public DateTime BeginsAt {
            get => _beginsAt.DefaultKind(DateTimeKind.Utc);
            set => _beginsAt = value.DefaultKind(DateTimeKind.Utc);
        }

        public DateTime? EndsAt {
            get => _endsAt?.DefaultKind(DateTimeKind.Utc);
            set => _endsAt = value.DefaultKind(DateTimeKind.Utc);
        }

        public double Duration { get; set; }

        public ChatContentType ContentType { get; set; }
        public string Content { get; set; } = "";
        public string? StreamId { get; set; }

        public DbChatEntry() { }
        public DbChatEntry(ChatEntry model) => UpdateFrom(model);

        public void UpdateFrom(ChatEntry model)
        {
            if (model.Id == 0)
                throw new ArgumentOutOfRangeException($"{nameof(model)}.{nameof(model.Id)}");
            Id = model.Id;
            ChatId = model.ChatId;
            CompositeId = GetCompositeId(model.ChatId, model.Id);
            Version = model.Version;
            AuthorId = model.AuthorId;
            BeginsAt = model.BeginsAt;
            EndsAt = model.EndsAt;
            ContentType = model.ContentType;
            Content = model.Content;
            StreamId = model.StreamId;
        }

        public ChatEntry ToModel()
            => new(ChatId, Id) {
                AuthorId = AuthorId,
                BeginsAt = BeginsAt,
                EndsAt = EndsAt,
                ContentType = ContentType,
                Content = Content,
                StreamId = StreamId ?? "",
            };
    }
}
