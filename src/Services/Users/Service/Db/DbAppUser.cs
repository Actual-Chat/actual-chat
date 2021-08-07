using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users.Db
{
    [Index(nameof(SpeakerId), IsUnique = true)]
    public class DbAppUser : DbUser
    {
        public string SpeakerId { get; set; } = "";
    }
}
