namespace ActualChat;

public static class MaintenanceModeExt
{
    public static void RequireNone(this MaintenanceMode mode, string resourceName)
    {
        if (mode == MaintenanceMode.None)
            return;

        var modeName = mode switch {
            MaintenanceMode.Import => "import",
            MaintenanceMode.Removal => "removal",
            _ => "maintenance",
        };
        throw StandardError.Constraint($"The {resourceName} is in {modeName} mode.");
    }
}
