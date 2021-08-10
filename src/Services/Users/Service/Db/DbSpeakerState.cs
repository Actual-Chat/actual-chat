using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl;
using Stl.Time;

namespace ActualChat.Users.Db
{
    [Table("SpeakerStates")]
    public class DbSpeakerState : IHasId<string>
    {
        private DateTime _lastOnlineAt;

        [Key] public string Id { get; set; } = "";

        public DateTime LastOnlineAt {
            get => _lastOnlineAt.DefaultKind(DateTimeKind.Utc);
            set => _lastOnlineAt = value.DefaultKind(DateTimeKind.Utc);
        }
    }
}
