using System.Security;

namespace ActualChat.Chat;

public static class ChatPermissionsExt
{
    public static bool Has(this ChatPermissions available, ChatPermissions required)
        => (available & required) == required;

    public static void Demand(this ChatPermissions available, ChatPermissions required)
    {
        if (!Has(available, required))
            throw NotEnoughPermissions(required);
    }

    public static Exception NotEnoughPermissions(ChatPermissions? required = null)
    {
        var message = "You can't perform this action: not enough permissions.";
        return required.HasValue
            ? new SecurityException($"{message} Requested permission: {required.Value}.")
            : new SecurityException(message);
    }
}
