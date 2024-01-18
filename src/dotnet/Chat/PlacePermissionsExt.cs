using System.Security;

namespace ActualChat.Chat;

public static class PlacePermissionsExt
{
    public static bool Has(this PlacePermissions available, PlacePermissions required)
        => (available & required) == required;

    public static void Require(this PlacePermissions available, PlacePermissions required)
    {
        if (!Has(available, required))
            throw NotEnoughPermissions(required);
    }

    public static Exception NotEnoughPermissions(PlacePermissions? required = null)
        => StandardError.NotEnoughPermissions(required?.ToString());
}
