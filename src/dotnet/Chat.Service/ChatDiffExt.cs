namespace ActualChat.Chat;

public static class ChatDiffExt
{
    internal static void ValidateForPlaceChat(this ChatDiff chatDiff)
    {
        if (chatDiff.IsTemplate == true)
            throw StandardError.Constraint("Can't set IsTemplate property on place chat.");
        if (chatDiff.TemplateId.HasValue)
            throw StandardError.Constraint("Can't set TemplateId property on place chat.");
        if (chatDiff.TemplatedForUserId.HasValue)
            throw StandardError.Constraint("Can't set TemplatedForUserId property on place chat.");
        if (chatDiff.AllowAnonymousAuthors == true)
            throw StandardError.Constraint("Can't set AllowAnonymousAuthors property on place chat.");
        if (chatDiff.AllowGuestAuthors == true)
            throw StandardError.Constraint("Can't set AllowGuestAuthors property on place chat.");
    }
}
