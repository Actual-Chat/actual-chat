using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;

namespace ActualChat.Chat.Db
{
    [Table("Chats")]
    public class DbChat : IHasId<string>, IHasVersion<long>
    {
        private DateTime _createdAt;

        [Key] public string Id { get; set; } = "";
        [ConcurrencyCheck] public long Version { get; set; }
        public string Title { get; set; } = "";
        public string AuthorId { get; set; } = "";
        public bool IsPublic { get; set; }

        public DateTime CreatedAt {
            get => _createdAt.DefaultKind(DateTimeKind.Utc);
            set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
        }

        public List<DbChatOwner> Owners { get; set; } = new();

        public Chat ToModel()
            => new(Id) {
                Title = Title,
                CreatedAt = CreatedAt,
                AuthorId = AuthorId,
                IsPublic = IsPublic,
                OwnerIds = Owners.Select(o => (UserId)o.UserId).ToImmutableArray(),
            };
    }
}
