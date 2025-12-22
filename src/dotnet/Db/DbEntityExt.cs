using ActualLab.Fusion.EntityFramework.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Db;

// NOTE(AY): This code requires C# 14, which isn't enabled on GitHub builders yet;
//           on the other hand, the type isn't used.

public static class DbEntityExt
{
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
        // Remove existing indexes on (State, DelayUntil) and (DelayUntil, State)
        var stateProp = events.Metadata.FindProperty(nameof(DbEvent.State));
        var delayUntilProp = events.Metadata.FindProperty(nameof(DbEvent.DelayUntil));
        if (stateProp != null && delayUntilProp != null) {
            var index1 = events.Metadata.FindIndex([stateProp, delayUntilProp]);
            if (index1 != null)
                events.Metadata.RemoveIndex(index1);

            var index2 = events.Metadata.FindIndex([delayUntilProp, stateProp]);
            if (index2 != null)
                events.Metadata.RemoveIndex(index2);
        }
        events.Property(e => e.Uuid).UseCollation("C");
        // 1. The MOST important index: partial index for pending (New) events
        events
            .HasIndex(e => e.DelayUntil)
            .IncludeProperties(e => new {e.Uuid, e.Version, e.LoggedAt, e.ValueJson, e.State})
            .HasDatabaseName("ix_events_pending")
            .HasFilter("state = 0");  // WHERE state = 0

        // Optional: if you also frequently query processed events (state = 1)
        events
            .HasIndex(e => e.DelayUntil)
            .IncludeProperties(e => new {e.Uuid, e.Version, e.LoggedAt, e.ValueJson, e.State})
            .HasDatabaseName("ix_events_processed")
            .HasFilter("state = 1");

        // Optional: if you need a covering index for state != 0 queries
        events
            .HasIndex(e => e.DelayUntil)
            .HasDatabaseName("ix_events_delay_until_non_new")
            .HasFilter("state != 0");
    }
}

