using System.Security;

namespace ActualChat.Chat;

public static class PermissionsExt
{
    public static void AssertHasPermissions(this ChatPermissions permissions, ChatPermissions requiredPermissions)
    {
        if (!CheckHasPermissions(permissions, requiredPermissions))
            Throw();
    }

    public static bool CheckHasPermissions(this ChatPermissions permissions, ChatPermissions requiredPermissions)
        => (permissions & requiredPermissions) == requiredPermissions;

    public static void Throw()
        => throw new SecurityException("Not enough permissions.");
}
