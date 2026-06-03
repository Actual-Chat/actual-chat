namespace ActualChat.UI.Blazor.Components;

public class TextInputOptions {
    public int Debounce { get; set; }
    public string Text { get; set; } = "";
    public string? CloseOnBlurSelector { get; set; }
}
