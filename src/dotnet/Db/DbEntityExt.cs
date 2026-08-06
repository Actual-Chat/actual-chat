using ActualLab.Fusion.EntityFramework.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Db;

#pragma warning disable CA1000 // Do not declare static members on generic types

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
            .HasDatabaseName("ix_events_pending")
            .HasFilter("state = 0");  // WHERE state = 0

        // Optional: if you need a covering index for state != 0 queries
        events
            .HasIndex(e => new {e.DelayUntil, e.State})
            .HasDatabaseName("ix_events_delay_until_state_non_new")
            .HasFilter("state != 0");
    }
}
