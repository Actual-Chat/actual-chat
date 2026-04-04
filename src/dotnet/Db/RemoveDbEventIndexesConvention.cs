using ActualLab.Fusion.EntityFramework.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace ActualChat.Db;

public class RemoveDbEventIndexesConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var entity = modelBuilder.Metadata.FindEntityType(typeof(DbEvent));
        if (entity == null)
            return;

        var indexes = entity.GetIndexes().ToList();
        foreach (var index in indexes)
        {
            var props = index.Properties;
            if (props.Count != 2)
                continue;

            var p0 = props[0].Name;
            var p1 = props[1].Name;

            if ((p0 != "State" || p1 != "DelayUntil") && (p0 != "DelayUntil" || p1 != "State"))
                continue; // Skip if not a State-DelayUntil index

            if (index.GetFilter() != null)
                continue; // Skip if it has a filter - it is new one!

            // Verify it's the attribute-generated index (usually has default name or matching properties)
            // We remove it unconditionally as per requirement.
            entity.RemoveIndex(index);
            Console.WriteLine($"Removing convention index: {index}");
        }
    }
}
