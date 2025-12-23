using ActualLab.Fusion.EntityFramework.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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

public static class DbEntityExt
{
    // NOTE(AY): This code requires C# 14, which isn't enabled on GitHub builders yet;
    //           on the other hand, the type isn't used.
    extension<TDbEntity, TModel>(IDbEntity<TDbEntity, TModel> entity)
        where TDbEntity : IDbEntity<TDbEntity, TModel>, new()
    {
        public static IDbEntity<TDbEntity, TModel> FromModel(TModel model)
        {
            var e = new TDbEntity();
            e.UpdateFrom(model);
            return e;
        }
    }


    public static void DefineIndexes(this EntityTypeBuilder<DbEvent> events)
    {
        // 1. The MOST important index: partial index for pending (New) events
        events
            .HasIndex(e => e.DelayUntil)
            .IncludeProperties(e => new {e.Uuid, e.Version, e.State})
            .HasDatabaseName("ix_events_pending")
            .HasFilter("state = 0");  // WHERE state = 0

        // Optional: if you need a covering index for state != 0 queries
        events
            .HasIndex(e => new {e.DelayUntil, e.State})
            .HasDatabaseName("ix_events_delay_until_state_non_new")
            .HasFilter("state != 0");
    }
}

