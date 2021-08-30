using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Stl;
using Stl.Time;
using Stl.Versioning;

namespace ActualChat.Chat.Db
{
    [Table("Chats")]
    public class DbChat : IHasId<string>, IHasMutableVersion<long>
    {
        private DateTime _createdAt;

        [Key] public string Id { get; set; } = "";
        [ConcurrencyCheck] public long Version { get; set; }
        public string Title { get; set; } = "";
        public string CreatorId { get; set; } = "";
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
                CreatorId = CreatorId,
                IsPublic = IsPublic,
                OwnerIds = Owners.Select(o => o.UserId).ToImmutableArray(),
            };
    }
}
