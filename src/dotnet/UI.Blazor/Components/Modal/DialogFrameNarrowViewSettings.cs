﻿namespace ActualChat.UI.Blazor.Components;

public enum DialogFramePosition { Bottom, Stretch }

public record DialogFrameNarrowViewSettings
{
    public static readonly DialogFrameNarrowViewSettings Bottom = new () { Position = DialogFramePosition.Bottom };
    public static readonly DialogFrameNarrowViewSettings Stretch = new () { Position = DialogFramePosition.Stretch };
    private bool _canSubmit = true;

    public DialogFramePosition Position { get; init; } = DialogFramePosition.Bottom;
    public bool? ShouldHideButtons { get; init; }
    public ButtonType SubmitButtonType { get; init; } = ButtonType.Button;
    public EventCallback SubmitClick { get; init; }
    public string SubmitButtonText { get; init; } = "";

    public bool CanSubmit {
        get => _canSubmit;
        set {
            if (_canSubmit == value)
                return;
            _canSubmit = value;
            RaiseCanSubmitChanged();
        }
    }

    public event EventHandler? CanSubmitChanged;

    public void RaiseCanSubmitChanged()
        => CanSubmitChanged?.Invoke(this, EventArgs.Empty);

    public bool? UseInteractiveHeader { get; init; }

    public bool IsSubmitDefined
        => SubmitButtonType != ButtonType.Button || SubmitClick.HasDelegate;
    internal bool ShouldUseInteractiveHeader
        => UseInteractiveHeader ?? IsSubmitDefined;

    public static DialogFrameNarrowViewSettings FormSubmitButton(string submitButtonText = "")
        => new() {
            Position = DialogFramePosition.Stretch,
            SubmitButtonType = ButtonType.Submit,
            SubmitButtonText = submitButtonText,
        };

    public static DialogFrameNarrowViewSettings SubmitButton(Action callback, string submitButtonText = "")
    {
        var eventCallback = callback.Target != null
            ? EventCallback.Factory.Create(callback.Target, callback)
            : new EventCallback(null, callback);
        return new DialogFrameNarrowViewSettings {
            Position = DialogFramePosition.Stretch,
            SubmitClick = eventCallback,
            SubmitButtonText = submitButtonText,
        };
    }

    public static DialogFrameNarrowViewSettings SubmitButton(Func<Task> callback, string submitButtonText = "")
        => SubmitButton(DialogFramePosition.Stretch, callback, submitButtonText);

    public static DialogFrameNarrowViewSettings SubmitButton(DialogFramePosition position, Func<Task> callback, string submitButtonText = "")
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
