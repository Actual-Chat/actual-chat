using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl;
using Stl.Time;

namespace ActualChat.Users.Db
{
    [Table("UserStates")]
    public class DbUserState : IHasId<string>
    {
        private DateTime _onlineCheckInAt;

        [Key] public string Id { get; set; } = "";

        public DateTime OnlineCheckInAt {
            get => _onlineCheckInAt.DefaultKind(DateTimeKind.Utc);
            set => _onlineCheckInAt = value.DefaultKind(DateTimeKind.Utc);
        }
    }
}
