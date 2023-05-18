namespace ActualChat.UI.Blazor.Components;

public enum DialogFramePosition { Bottom, Stretch }

public record DialogFrameNarrowViewSettings
{
    public static readonly DialogFrameNarrowViewSettings Bottom = new () { Position = DialogFramePosition.Bottom };
    public static readonly DialogFrameNarrowViewSettings Stretch = new () { Position = DialogFramePosition.Stretch };

    public DialogFramePosition Position { get; init; } = DialogFramePosition.Bottom;
    public bool? ShouldHideButtons { get; init; }
    public ButtonType SubmitButtonType { get; init; } = ButtonType.Button;
    public EventCallback SubmitClick { get; init; }
    public string SubmitButtonText { get; init; } = "Save";

    public bool IsSubmitDefined
        => SubmitButtonType != ButtonType.Button || SubmitClick.HasDelegate;

    public static DialogFrameNarrowViewSettingsBuilder CreateBuilder(DialogFramePosition position)
        => new (position == DialogFramePosition.Bottom ? Bottom : Stretch);
}

public readonly struct DialogFrameNarrowViewSettingsBuilder
{
    private readonly DialogFrameNarrowViewSettings _settings;

    public DialogFrameNarrowViewSettings Build()
        => _settings;

    public DialogFrameNarrowViewSettingsBuilder SubmitText(string text)
        => new (_settings with {SubmitButtonText = text});

    public DialogFrameNarrowViewSettingsBuilder SubmitButtonType(ButtonType buttonType)
        => new (_settings with {SubmitButtonType = buttonType});

    public DialogFrameNarrowViewSettingsBuilder SubmitHandler(Action callback)
    {
        var eventCallback = callback.Target != null
            ? EventCallback.Factory.Create(callback.Target, callback)
            : new EventCallback(null, callback);
        return new DialogFrameNarrowViewSettingsBuilder(_settings with {SubmitClick = eventCallback});
    }

    public DialogFrameNarrowViewSettingsBuilder SubmitHandler(Func<Task> callback)
    {
        var eventCallback = callback.Target != null
            ? EventCallback.Factory.Create(callback.Target, callback)
            : new EventCallback(null, callback);
        return new DialogFrameNarrowViewSettingsBuilder(_settings with {SubmitClick = eventCallback});
    }
    public DialogFrameNarrowViewSettingsBuilder ShouldHideButtons(bool? hide)
        => new (_settings with {ShouldHideButtons = hide});

    internal DialogFrameNarrowViewSettingsBuilder(DialogFrameNarrowViewSettings settings)
        => _settings = settings;
}
