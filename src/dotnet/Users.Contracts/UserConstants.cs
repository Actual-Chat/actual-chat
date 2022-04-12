namespace ActualChat.Users;

public static class UserConstants
{
    public static class Admin
    {
        public static readonly string UserId = "admin";
        public static readonly string Name = "Admin";
        public static readonly string Picture = "https://avatars.dicebear.com/api/avataaars/12333323132.svg";
#pragma warning disable CA2211
        public static Session Session = null!;
    }

    public static class Claims
    {
        public static string Status => "urn:actual.chat:status";
    }
}
