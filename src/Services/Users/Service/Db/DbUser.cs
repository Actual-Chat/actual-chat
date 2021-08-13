using System;
using Stl.Fusion.Authentication;
using Stl.Fusion.EntityFramework.Authentication;
using Stl.Time;

namespace ActualChat.Users.Db
{
    public class DbUser : DbUser<string>
    {
        private DateTime _createdAt;

        public DateTime CreatedAt {
            get => _createdAt.DefaultKind(DateTimeKind.Utc);
            set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
        }

        public override User ToModel(IServiceProvider services)
        {
            return base.ToModel(services);
        }
    }
}
