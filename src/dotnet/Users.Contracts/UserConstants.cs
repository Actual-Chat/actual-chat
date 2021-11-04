namespace ActualChat.Users;

public static class UserConstants
{
    public static class Admin
    {
        public static readonly UserId UserId = "admin";
        public static readonly AuthorId AuthorId = "admin";
        public static readonly string Picture = "//eu.ui-avatars.com/api/?background=01BAEF&bold=true&length=1&name=Admin";
        public static readonly string Name = "Admin";
        public static readonly string Nickname = "Admin";
#pragma warning disable CA2211
        public static Session Session = null!;
    }
}
