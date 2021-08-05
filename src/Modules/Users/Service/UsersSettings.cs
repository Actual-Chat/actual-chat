using Stl.DependencyInjection;

namespace ActualChat.Users
{
    [RegisterSettings("ActualChat.Users")]
    public class UsersSettings
    {
        // DBs
        public string Db { get; set; } =
            "Server=localhost;Database=ac_dev_users;Port=5432;User Id=postgres;Password=ActualChat.Dev.2021.07";
    }
}
