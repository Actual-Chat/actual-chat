using ActualChat.Module;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Db.Module;

#pragma warning disable CA2255

internal static class DbModuleInitializer
{
    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        CoreModuleInitializer.Load();
        DbLogEntrySerializer.Default = DbLogEntrySerializer.Default with {
            Format = DataFormat.Bytes,
        };
    }
}
