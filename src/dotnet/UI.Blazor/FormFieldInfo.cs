namespace ActualChat.UI.Blazor;

public abstract class FormFieldInfo
{
    private static ConcurrentDictionary<Type, FormFieldInfo[]> FieldInfoCache = new();

    public static readonly string FieldIdSuffix = "FieldId";

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type FormType { get; }
    public PropertyInfo Property { get; }
    public PropertyInfo FieldIdProperty { get; }
    public Action<FormModel, object> UntypedSetter { get; }
    public Func<FormModel, object> UntypedGetter { get; }
    public Action<FormModel, string> FieldIdSetter { get; }
    public Action<FormModel, FormModel> Copier { get; init; } = null!;

#pragma warning disable IL2070
    [UnconditionalSuppressMessage("Trimming", "IL2070:DoesNotSatisfyDynamicallyAccessedMemberTypes.PublicProperties", Justification = "FormType is marked with DynamicallyAccessedMembers.")]
    [UnconditionalSuppressMessage("Trimming", "IL2111:DoesNotSatisfyDynamicallyAccessedMemberTypes.PublicProperties", Justification = "FormType is marked with DynamicallyAccessedMembers.")]
    public static FormFieldInfo[] GetFields(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type formType)
        => FieldInfoCache.GetOrAdd(formType, static ([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]formType1) => {
            var fields = new List<FormFieldInfo>();
            foreach (var fieldIdProperty in formType1.GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
                if (fieldIdProperty.PropertyType != typeof(string))
                    continue;
                if (!fieldIdProperty.Name.OrdinalEndsWith(FieldIdSuffix))
                    continue;
                var field = New(formType1, fieldIdProperty);
                fields.Add(field);
            }
            return fields.ToArray();
        });
#pragma warning restore IL2070

    [UnconditionalSuppressMessage("Trimming", "IL2076:DoesNotSatisfyDynamicallyAccessedMemberTypes.PublicProperties", Justification = "FormType is marked with DynamicallyAccessedMembers.")]
    [UnconditionalSuppressMessage("Trimming", "IL2111:DoesNotSatisfyDynamicallyAccessedMemberTypes.PublicProperties", Justification = "FormType is marked with DynamicallyAccessedMembers.")]
    public static FormFieldInfo New([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]Type formType, PropertyInfo fieldIdProperty)
    {
        if (!fieldIdProperty.Name.OrdinalEndsWith(FieldIdSuffix))
            throw new ArgumentOutOfRangeException(nameof(fieldIdProperty),
                $"'{fieldIdProperty.Name}' must end with '{FieldIdSuffix}'.");
        if (fieldIdProperty.PropertyType != typeof(string))
            throw new ArgumentOutOfRangeException(nameof(fieldIdProperty),
                $"'{fieldIdProperty.Name}' must be of string type.");

        var propertyName = fieldIdProperty.Name[..^FieldIdSuffix.Length];
        var property = formType.GetProperty(propertyName)
            ?? throw new ArgumentOutOfRangeException(nameof(fieldIdProperty),
                $"No field that corresponds to '{fieldIdProperty.Name}'.");

        var field = (FormFieldInfo) typeof(FormFieldInfo<>)
            .MakeGenericType(property.PropertyType)
            .CreateInstance(formType, property, fieldIdProperty);
        return field;
    }

    protected FormFieldInfo([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]Type formType, PropertyInfo property, PropertyInfo fieldIdProperty)
    {
        FormType = formType;
        Property = property;
        FieldIdProperty = fieldIdProperty;
        UntypedSetter = Property.GetSetter<object>(true);
        UntypedGetter = Property.GetGetter<object>(true);
        FieldIdSetter = FieldIdProperty.GetSetter<string>(true);
    }
}

public sealed class FormFieldInfo<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]T> : FormFieldInfo
{
    public Action<FormModel, T> Setter { get; }
    public Func<FormModel, T> Getter { get; }

    public FormFieldInfo([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]Type formType, PropertyInfo property, PropertyInfo fieldIdProperty)
        : base(formType, property, fieldIdProperty)
    {
        Setter = Property.GetSetter<T>();
        Getter = Property.GetGetter<T>();
        Copier = (source, target) => {
            var value = Getter.Invoke(source);
            Setter.Invoke(target, value);
        };
    }
}
