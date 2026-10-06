namespace ActualChat;

public static class MaintenanceModeExt
{
    public static void RequireNone(this MaintenanceMode mode, string resourceName)
    {
        if (mode == MaintenanceMode.None)
            return;

        var modeName = mode == MaintenanceMode.Import ? "import" : "maintenance";
        throw StandardError.Constraint($"The {resourceName} is in {modeName} mode.");
    }
}
