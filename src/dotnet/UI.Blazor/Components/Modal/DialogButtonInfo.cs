namespace ActualChat.UI.Blazor.Components;

public class DialogButtonInfo
{
    public static readonly DialogButtonInfo BackButton = new() {
        Title = "Back",
        IsCancel = true
    };
    public static readonly DialogButtonInfo CancelButton = new() {
        Title = "Cancel",
        IsCancel = true
    };
    public static readonly DialogButtonInfo CloseButton = new() {
        Title = "Close",
        IsCancel = true
    };

    private bool _canExecute = true;

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
        get => _canExecute;
        set {
            if (_canExecute == value)
                return;
            _canExecute = value;
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
