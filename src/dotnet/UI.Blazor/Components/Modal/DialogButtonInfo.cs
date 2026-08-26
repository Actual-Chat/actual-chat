using ActualChat.Localization;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.Components;

public class DialogButtonInfo
{
    // Titles are read per call rather than cached: the UI language can change at runtime
    public static DialogButtonInfo CreateBackButton(IStringLocalizer l) => new() {
        Title = l.Common_Back,
        IsCancel = true,
    };
    public static DialogButtonInfo CreateCancelButton(IStringLocalizer l) => new() {
        Title = l.Common_Cancel,
        IsCancel = true,
    };
    public static DialogButtonInfo CreateCloseButton(IStringLocalizer l) => new() {
        Title = l.Common_Close,
        IsCancel = true,
    };

    public static DialogButtonInfo CreateSubmitButton(string title, Func<Task> execute) => new() {
        Title = title,
        IsSubmit = true,
        Execute = execute
    };

    public static DialogButtonInfo CreateSubmitButton(string title, Action execute)
        => CreateSubmitButton(title, () => { execute(); return Task.CompletedTask; });

    public string Title { get; init; } = "";
    public bool IsCancel { get; init; }
    public bool IsSubmit { get; init; }
    public bool IsDestructive { get; init; }

    public Func<Task>? Execute { get; init; }

    public bool CanExecute {
        get;
        set {
            if (field == value)
                return;

            field = value;
            RaiseCanExecuteChanged();
        }
    } = true;

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
