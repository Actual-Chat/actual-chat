using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace ActualChat.UI.Blazor.Components;

public sealed class ValidationModelStore
{
    private readonly ConcurrentDictionary<Type, Dictionary<string, ValidatedProperty>> _cache = new ();

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be defined in assemblies that do not get trimmed.")]
    public IReadOnlyCollection<PropertyValidationContext> List(ValidationContext validationContext)
    {
        var result = new List<PropertyValidationContext>();
        foreach (var property in _cache.GetOrAdd(validationContext.ObjectType, BuildModel).Values) {
            var ctx = GetForProperty(validationContext, property);
            result.Add(ctx);
        }
        return result;
    }

    public PropertyValidationContext? Get(string propertyName, ValidationContext validationContext)
    {
        var property = _cache.GetOrAdd(validationContext.ObjectType, BuildModel)!.GetValueOrDefault(validationContext.MemberName);
        if (property is null)
            return null;

        return GetForProperty(validationContext, property);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be defined in assemblies that do not get trimmed.")]
    private static PropertyValidationContext GetForProperty(ValidationContext validationContext, ValidatedProperty property)
    {
        var propertyValue = property.Property.GetValue(validationContext.ObjectInstance);
        var context = new ValidationContext(validationContext.ObjectInstance, validationContext, validationContext.Items) {
            MemberName = property.Property.Name,
        };
        var ctx = new PropertyValidationContext(context, propertyValue, property);
        return ctx;
    }

    private static Dictionary<string, ValidatedProperty> BuildModel(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type modelType)
        => (from property in modelType.GetProperties()
            let asyncValidationAttributes = property.GetCustomAttributes<AsyncValidationAttribute>().ToList()
            where asyncValidationAttributes.Count != 0
            select new ValidatedProperty(property, asyncValidationAttributes)).ToDictionary(x => x.Property.Name, StringComparer.Ordinal);

    public sealed record ValidatedProperty(PropertyInfo Property, IReadOnlyList<AsyncValidationAttribute> AsyncAttributes);

    public sealed record PropertyValidationContext(
        ValidationContext ValidationContext,
        object? Value,
        ValidatedProperty Property);
}
