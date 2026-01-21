namespace ActualChat.Users.Db;

public sealed class DbUserIdHandler
{
    public string Parse(string userId, bool allowNone)
    {
        if (!TryParse(userId, true, out var result))
            throw StandardError.Constraint("Invalid UserId.");
        if (!allowNone && result.IsNullOrEmpty())
            throw StandardError.Constraint("UserId is required.");
        return result;
    }

    public bool TryParse(string userId, bool allowNone, out string result)
    {
        result = "";
        if (userId.IsNullOrEmpty())
            return allowNone;

        result = userId;
        return true;
    }
}
