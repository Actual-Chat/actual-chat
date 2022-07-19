namespace ActualChat.Users;

public static class UserOverrides
{
    public static void Apply()
    {
        User.MustExist = Requirement.New(
            new(() => new NoAccountException()),
            (User? u) => u != null);
        User.MustBeAuthenticated = Requirement.New(
            new(() => new NoAccountException()),
            (User? u) => u?.IsAuthenticated() == true);
    }
}
