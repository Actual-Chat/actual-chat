namespace ActualChat.UI.Blazor;

public static class JSRuntimeExt
{
    // EvalXxx

    public static ValueTask EvalVoid(this IJSRuntime js, string expression, CancellationToken cancellationToken = default)
        => js.InvokeVoidAsync("eval", cancellationToken, expression);

    public static ValueTask<T> Eval<T>(this IJSRuntime js, string expression, CancellationToken cancellationToken = default)
        => js.InvokeAsync<T>("eval", cancellationToken, expression);
}
