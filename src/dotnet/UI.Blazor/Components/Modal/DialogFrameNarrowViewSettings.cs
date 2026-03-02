namespace ActualChat.UI.Blazor.Components;

public enum DialogFramePosition
{
    Default = 0, // Default
    Bottom,
}

public sealed record DialogFrameNarrowViewSettings
{
    public static readonly DialogFrameNarrowViewSettings Default = new ();
    public static readonly DialogFrameNarrowViewSettings Bottom = new () { Position = DialogFramePosition.Bottom };

    public DialogFramePosition Position { get; init; }
    public bool? ShouldHideButtons { get; init; }
    public ButtonType SubmitButtonType { get; init; } = ButtonType.Button;
    public string SubmitButtonText { get; init; } = "";
    public EventCallback SubmitClick { get; init; }

    public bool CanSubmit {
        get;
        set {
            if (field == value)
                return;

            field = value;
            RaiseCanSubmitChanged();
        }
    } = true;

    public bool HasSubmit => SubmitButtonType != ButtonType.Button || SubmitClick.HasDelegate;
    public bool? UseInteractiveHeader { get; init; }
    public bool ShouldUseInteractiveHeader => UseInteractiveHeader ?? HasSubmit;
    public event EventHandler? CanSubmitChanged;

    public void RaiseCanSubmitChanged()
        => CanSubmitChanged?.Invoke(this, EventArgs.Empty);

    public static DialogFrameNarrowViewSettings ForFormSubmitButton(string submitButtonText = "")
        => new() {
            SubmitButtonType = ButtonType.Submit,
            SubmitButtonText = submitButtonText,
        };

    public static DialogFrameNarrowViewSettings ForSubmitButton(Action callback, string submitButtonText = "")
    {
        var eventCallback = callback.Target != null
            ? EventCallback.Factory.Create(callback.Target, callback)
            : new EventCallback(null, callback);
        return new DialogFrameNarrowViewSettings {
            SubmitClick = eventCallback,
            SubmitButtonText = submitButtonText,
        };
    }

    public static DialogFrameNarrowViewSettings ForSubmitButton(Func<Task> callback, string submitButtonText = "")
        => ForSubmitButton(DialogFramePosition.Default, callback, submitButtonText);

    public static DialogFrameNarrowViewSettings ForSubmitButton(DialogFramePosition position, Func<Task> callback, string submitButtonText = "")
    {
        var eventCallback = callback.Target != null
            ? EventCallback.Factory.Create(callback.Target, callback)
            : new EventCallback(null, callback);
        return new DialogFrameNarrowViewSettings {
            Position = position,
            SubmitClick = eventCallback,
            SubmitButtonText = submitButtonText,
        };
    }
}
