using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor;

public abstract class FormModel
{
    protected ComponentIdGenerator? ComponentIdGenerator { get; }

    public FormFieldInfo[] Fields { get; }
    public string FormName { get; }
    public string FormId { get; set; } = "";

    protected FormModel(string formName, ComponentIdGenerator? componentIdGenerator = null)
    {
        FormName = formName;
        ComponentIdGenerator = componentIdGenerator;
        Fields = FormFieldInfo.GetFields(GetType());
        RenewIds();
    }

    protected void RenewIds()
    {
        FormId = ComponentIdGenerator?.Next(FormName) ?? FormName;
        foreach (var field in Fields) {
            var labelId = $"{FormId}-{field.Property.Name}";
            field.FieldIdSetter.Invoke(this, labelId);
        }
    }

    public void CopyTo(FormModel other)
    {
        if (other.GetType() != GetType())
            throw new ArgumentOutOfRangeException(nameof(other), "Forms should be of the same type.");
        foreach (var field in Fields)
            field.Copier.Invoke(this, other);
    }
}

public abstract class FormModel<TFormModel> : FormModel
    where TFormModel : FormModel
{
    public TFormModel Base { get; set; } = null!;

    public TFormModel CopyToBase()
    {
        if (Base != null!) {
            CopyTo(Base);
            return (TFormModel) (object) this;
        }

        Base = (TFormModel)MemberwiseClone();
        if (Base is FormModel<TFormModel> typedBase)
            typedBase.Base = null!;
        return (TFormModel) (object) this;
    }

    public TFormModel CopyFromBase()
    {
        Base.CopyTo(this);
        return (TFormModel) (object) this;
    }

    protected FormModel(string formName, ComponentIdGenerator? componentIdGenerator = null)
        : base(formName, componentIdGenerator)
    { }
}
