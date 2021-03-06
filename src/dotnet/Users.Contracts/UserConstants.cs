namespace ActualChat.Users;

public static class UserConstants
{
    public static class Admin
    {
        public static string UserId { get; } = "admin";
        public static string Name { get; } =  "Admin";
        public static string Picture { get; } = "https://avatars.dicebear.com/api/avataaars/12333323132.svg";
        public static Session Session { get; set; } = Session.Null;
    }

    public static class Claims
    {
        public static string Status => "urn:actual.chat:status";
    }
}
