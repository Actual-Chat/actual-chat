using System;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users.Db
{
    public class DbAppUserBySpeakerIdResolver : DbEntityResolver<UsersDbContext, string, DbAppUser>
    {
        public DbAppUserBySpeakerIdResolver(IServiceProvider services)
            : base(new Options() { KeyPropertyName = "SpeakerId" }, services)
        { }
    }
}
