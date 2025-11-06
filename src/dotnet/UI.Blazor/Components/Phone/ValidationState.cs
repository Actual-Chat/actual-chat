namespace ActualChat.UI.Blazor.Components;

public enum ValidationState {
    None = 0,
    Validating,
    Valid,
    Invalid,
    CannotSend,
    CodeSentRecently,
}
