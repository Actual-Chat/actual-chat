namespace ActualChat.UI.Blazor.App.Components;

public readonly record struct AttachmentId
{
    public Guid Value { get; }

    private AttachmentId(Guid value) => Value = value;

    public static AttachmentId New() => new(Guid.NewGuid());

    public static AttachmentId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse(string value, out AttachmentId result)
    {
        if (Guid.TryParse(value, out var guid)) {
            result = new AttachmentId(guid);
            return true;
        }
        result = default;
        return false;
    }

    public override string ToString() => Value.ToString();
}
