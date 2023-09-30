using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Diff.Handlers;

public class RecordDiffHandler<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRecord,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDiff> : DiffHandlerBase<TRecord, TDiff>
    where TRecord : class
    where TDiff : RecordDiff, new()
{
    public ApiArray<RecordDiffPropertyInfo> Properties { get; init; }
    public Func<TRecord, TRecord> Cloner { get; init; }

    public RecordDiffHandler(DiffEngine engine) : base(engine)
    {
        var tRecord = typeof(TRecord);
        var tDiff = typeof(TDiff);

        var properties = new List<RecordDiffPropertyInfo>();
        foreach (var pDiff in tDiff.GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
            var pRecord = tRecord.GetProperty(pDiff.Name, BindingFlags.Instance | BindingFlags.Public);
            if (pRecord == null)
                continue; // We omit extra properties in diff assuming they're handled manually
            var property = (RecordDiffPropertyInfo) typeof(RecordDiffPropertyInfo<,>)
                .MakeGenericType(typeof(TRecord), typeof(TDiff), pDiff.PropertyType, pRecord.PropertyType)
                .CreateInstance(Engine, pDiff, pRecord);
            properties.Add(property);
        }
        Properties = properties.ToApiArray();
        Cloner = ObjectExt.GetCloner<TRecord>();
    }

    public override TDiff Diff(TRecord source, TRecord target)
    {
        var diff = new TDiff();
        foreach (var property in Properties)
            property.Diff(source, target, diff);
        return diff;
    }

    public override TRecord Patch(TRecord source, TDiff diff)
    {
        var target = Cloner.Invoke(source);
        foreach (var property in Properties)
            property.Apply(source, target, diff);
        return target;
    }

    // Nested types

    public abstract class RecordDiffPropertyInfo(
        DiffEngine engine,
        PropertyInfo diffProperty,
        PropertyInfo recordProperty)
    {
        public DiffEngine Engine { get; } = engine;
        public PropertyInfo DiffProperty { get; } = diffProperty;
        public PropertyInfo RecordProperty { get; } = recordProperty;
        public abstract IDiffHandler UntypedHandler { get; }

        public abstract void Diff(TRecord source, TRecord target, TDiff diff);
        public abstract void Apply(TRecord source, TRecord target, TDiff diff);
    }

    public class RecordDiffPropertyInfo<TDiffProperty, TRecordProperty> : RecordDiffPropertyInfo
    {
        public IDiffHandler<TRecordProperty, TDiffProperty> Handler { get; }
        public override IDiffHandler UntypedHandler => Handler;
        public Func<object, TDiffProperty> DiffPropertyGetter { get; }
        public Action<object, TDiffProperty> DiffPropertySetter { get; }
        public Func<object, TRecordProperty> RecordPropertyGetter { get; }
        public Action<object, TRecordProperty>? RecordPropertySetter { get; }

        public RecordDiffPropertyInfo(DiffEngine engine, PropertyInfo diffProperty, PropertyInfo recordProperty)
            : base(engine, diffProperty, recordProperty)
        {
            Handler = Engine.GetHandler<TRecordProperty, TDiffProperty>();
            DiffPropertyGetter = DiffProperty.GetGetter<TDiffProperty>();
            DiffPropertySetter = DiffProperty.GetSetter<TDiffProperty>();
            RecordPropertyGetter = RecordProperty.GetGetter<TRecordProperty>();
            if (RecordProperty.SetMethod != null)
                RecordPropertySetter = RecordProperty.GetSetter<TRecordProperty>();
        }

        public override void Diff(TRecord source, TRecord target, TDiff diff)
        {
            var sourceValue = RecordPropertyGetter.Invoke(source);
            var targetValue = RecordPropertyGetter.Invoke(target);
            var diffValue = Handler.Diff(sourceValue, targetValue);
            DiffPropertySetter.Invoke(diff, diffValue);
        }

        public override void Apply(TRecord source, TRecord target, TDiff diff)
        {
            if (RecordPropertySetter == null)
                return;

            var sourceValue = RecordPropertyGetter.Invoke(source);
            var diffValue = DiffPropertyGetter.Invoke(diff);
            var targetValue = Handler.Patch(sourceValue, diffValue);
            RecordPropertySetter.Invoke(target, targetValue);
        }
    }
}
