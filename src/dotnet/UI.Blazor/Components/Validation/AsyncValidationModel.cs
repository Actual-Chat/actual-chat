using System.ComponentModel.DataAnnotations;

namespace ActualChat.UI.Blazor.Components;

public static class AsyncValidationModel
{
    private static readonly ConcurrentDictionary<Type, ValidatedType> Cache = new ();

    public static ValidatedType Get(Type modelType)
        => Cache.GetOrAdd(modelType, BuildModel);

    public static PropertyValidationContext? CreatePropertyValidationContext(ValidationContext validationContext)
        => Get(validationContext.ObjectType).CreatePropertyValidationContext(validationContext);

    // Private methods

    private static ValidatedType BuildModel(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
    {
        var properties = (
            from property in type.GetProperties()
            let validationAttributes = property.GetCustomAttributes<ValidationAttribute>().ToList()
            let asyncValidationAttributes = validationAttributes.OfType<AsyncValidationAttribute>().ToList()
            where validationAttributes.Count != 0
            select new ValidatedProperty(property, asyncValidationAttributes, validationAttributes)
            ).ToDictionary(x => x.Property.Name);
        return new ValidatedType(type, properties);
    }

    // Nested types

    public sealed record ValidatedType(
        Type Type,
        IReadOnlyDictionary<string, ValidatedProperty> Properties)
    {
        public IReadOnlyDictionary<string, ValidatedProperty> AsyncOnlyProperties { get; }
            = Properties.Where(x => x.Value.AsyncAttributes.Count != 0).ToDictionary(x => x.Key, x => x.Value);

        public ValidatedProperty? this[string? propertyName]
            => propertyName is null ? null : Properties.GetValueOrDefault(propertyName);

        public PropertyValidationContext[] CreateAsyncPropertyValidationContexts(ValidationContext validationContext)
        {
            var result = new PropertyValidationContext[AsyncOnlyProperties.Count];
            var i = 0;
            foreach (var property in AsyncOnlyProperties.Values)
                result[i++] = CreatePropertyValidationContext(validationContext, property);
            return result;
        }

        // ReSharper disable once MemberHidesStaticFromOuterClass
        public PropertyValidationContext? CreatePropertyValidationContext(ValidationContext validationContext)
        {
            var property = this[validationContext.MemberName];
            return property is null ? null : CreatePropertyValidationContext(validationContext, property);
        }

        public static PropertyValidationContext CreatePropertyValidationContext(
            ValidationContext validationContext, ValidatedProperty property)
        {
            var propertyValue = property.Property.GetValue(validationContext.ObjectInstance);
            var context = new ValidationContext(
                validationContext.ObjectInstance, validationContext, validationContext.Items) {
                MemberName = property.Property.Name,
            };
            return new PropertyValidationContext(context, property, propertyValue);
        }
    }

    public sealed record ValidatedProperty(
        PropertyInfo Property,
        IReadOnlyList<AsyncValidationAttribute> AsyncAttributes,
        IReadOnlyList<ValidationAttribute> Attributes)
    {
        private DisplayAttribute? Display { get; } = Property.GetCustomAttribute<DisplayAttribute>();

        // Resolved per call rather than cached: [Display(ResourceType)] must follow the current language.
        public string DisplayName => Display?.GetName() ?? Property.Name;
    }

    public sealed record PropertyValidationContext(
        ValidationContext ValidationContext,
        ValidatedProperty Property,
        object? Value);
}
