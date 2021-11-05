namespace ActualChat.Users;

public static class UserConstants
{
    public static class Admin
    {
        public static readonly UserId UserId = "admin";
        public static readonly string Name = "Admin";
        public static readonly string Picture = "//eu.ui-avatars.com/api/?background=01BAEF&bold=true&length=1&name=Admin";
#pragma warning disable CA2211
        public static Session Session = null!;
    }
}
